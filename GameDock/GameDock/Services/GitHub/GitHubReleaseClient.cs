using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameDock.Services.GitHub;

/// <summary>Anonymous client for <c>api.github.com/repos/{owner}/{repo}/releases/latest</c>.</summary>
public sealed class GitHubReleaseClient : IGitHubReleaseClient, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _http;

    public GitHubReleaseClient()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

        // Timeouts are controlled per call with cancellation tokens (downloads may take a while).
        _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        // GitHub rejects API requests without a User-Agent.
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("GameDock", VersionHelper.Normalize(version).ToString()));
    }

    public async Task<ReleaseInfo?> GetLatestReleaseAsync(
        string owner,
        string repository,
        Regex installerPattern,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentNullException.ThrowIfNull(installerPattern);

        var uri = new Uri($"https://api.github.com/repos/{owner}/{repository}/releases/latest");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // No published release yet.
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (IsRateLimited(response, out var resetsAt))
            throw new GitHubRateLimitException(resetsAt);

        response.EnsureSuccessStatusCode();

        await using var json = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var release = await JsonSerializer.DeserializeAsync<ReleaseDto>(json, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        if (release is null || release.Draft || release.Prerelease ||
            !VersionHelper.TryParseTag(release.TagName, out var version))
            return null;

        Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var pageUrl);

        return new ReleaseInfo(
            version,
            release.TagName!,
            release.PublishedAt,
            pageUrl,
            SelectInstaller(release.Assets, installerPattern));
    }

    public async Task<string> DownloadAsync(
        ReleaseAsset asset,
        string folder,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        Directory.CreateDirectory(folder);
        var targetPath = Path.Combine(folder, Path.GetFileName(asset.Name));
        var partialPath = targetPath + ".part";

        try
        {
            using (var response = await _http
                       .GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                       .ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                var total = response.Content.Headers.ContentLength ?? asset.Size;
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var target = new FileStream(
                    partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

                var buffer = new byte[81920];
                long received = 0;
                int read;
                progress?.Report(new DownloadProgress(0, total));

                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    received += read;
                    progress?.Report(new DownloadProgress(received, total));
                }
            }

            await VerifyAsync(partialPath, asset, cancellationToken).ConfigureAwait(false);

            File.Move(partialPath, targetPath, overwrite: true);
            return targetPath;
        }
        catch
        {
            TryDelete(partialPath);
            throw;
        }
    }

    public void Dispose() => _http.Dispose();

    private static bool IsRateLimited(HttpResponseMessage response, out DateTimeOffset? resetsAt)
    {
        resetsAt = null;
        if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests))
            return false;

        if (!response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) ||
            remaining.FirstOrDefault() != "0")
            return response.StatusCode == HttpStatusCode.TooManyRequests;

        if (response.Headers.TryGetValues("x-ratelimit-reset", out var reset) &&
            long.TryParse(reset.FirstOrDefault(), out var seconds))
            resetsAt = DateTimeOffset.FromUnixTimeSeconds(seconds);

        return true;
    }

    private static async Task VerifyAsync(string path, ReleaseAsset asset, CancellationToken cancellationToken)
    {
        var length = new FileInfo(path).Length;
        if (asset.Size > 0 && length != asset.Size)
            throw new InvalidDataException($"Downloaded file has {length} bytes, expected {asset.Size}.");

        if (asset.Sha256 is null)
            return;

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));

        if (!string.Equals(hash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Downloaded file failed the SHA-256 check.");
    }

    private static ReleaseAsset? SelectInstaller(IReadOnlyList<AssetDto>? assets, Regex pattern)
    {
        var asset = assets?.FirstOrDefault(a => a.Name is not null && pattern.IsMatch(a.Name));
        if (asset is null || !Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var url))
            return null;

        return new ReleaseAsset(Path.GetFileName(asset.Name!), url, asset.Size, ParseSha256(asset.Digest));
    }

    /// <summary>GitHub publishes asset digests as <c>sha256:&lt;hex&gt;</c>.</summary>
    private static string? ParseSha256(string? digest)
    {
        const string prefix = "sha256:";
        return digest is not null && digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? digest[prefix.Length..]
            : null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not delete {path}: {ex.Message}");
        }
    }

    // ===== GitHub API payload (only the fields we use) =====

    private sealed record ReleaseDto(
        string? TagName,
        string? HtmlUrl,
        bool Draft,
        bool Prerelease,
        DateTimeOffset? PublishedAt,
        IReadOnlyList<AssetDto>? Assets);

    private sealed record AssetDto(
        string? Name,
        string? BrowserDownloadUrl,
        long Size,
        string? Digest);
}
