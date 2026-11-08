using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
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

    /// <summary>Pauses between download attempts; one more attempt than there are delays.</summary>
    private static readonly TimeSpan[] DownloadRetryDelays =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

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

        // Never resume from a leftover of an earlier, unrelated download.
        TryDelete(partialPath);

        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await DownloadToFileAsync(asset, partialPath, progress, cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (Exception ex) when (attempt < DownloadRetryDelays.Length + 1 &&
                                           IsTransientDownloadError(ex, cancellationToken))
                {
                    // GitHub's release CDN regularly answers 502/503/504 for a short while (especially right
                    // after an asset is published) or drops the connection: wait and continue where we stopped.
                    Debug.WriteLine($"Download attempt {attempt} of {asset.Name} failed: {ex.Message}");
                    await Task.Delay(DownloadRetryDelays[attempt - 1], cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Downloads <paramref name="asset"/> into <paramref name="path"/>, resuming with an HTTP range request
    /// when the file already holds part of it and the server supports ranges.
    /// </summary>
    private async Task DownloadToFileAsync(
        ReleaseAsset asset,
        string path,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var existing = File.Exists(path) ? new FileInfo(path).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
        if (existing > 0)
            request.Headers.Range = new RangeHeaderValue(existing, null);

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        // The previous attempt already received the whole file.
        if (existing > 0 && existing == asset.Size &&
            response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            return;

        EnsureDownloadSucceeded(response);

        // Server ignored the range (200 instead of 206): start over.
        var resume = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        var received = resume ? existing : 0;
        var total = asset.Size > 0
            ? asset.Size
            : response.Content.Headers.ContentLength is { } length ? length + received : 0;

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(
            path, resume ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        int read;
        progress?.Report(new DownloadProgress(received, total));

        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            progress?.Report(new DownloadProgress(received, total));
        }
    }

    private static void EnsureDownloadSucceeded(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var code = (int)response.StatusCode;
        var message = code >= 500
            ? $"GitHub's download server is not responding ({code} {response.ReasonPhrase}). Please try again in a few minutes."
            : $"Download failed: {code} {response.ReasonPhrase}.";

        throw new HttpRequestException(message, null, response.StatusCode);
    }

    /// <summary>Server-side hiccups and dropped connections that are worth another attempt.</summary>
    private static bool IsTransientDownloadError(Exception ex, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        return ex switch
        {
            HttpRequestException { StatusCode: null } => true, // DNS / connection failure
            HttpRequestException { StatusCode: { } status } =>
                status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500,
            HttpIOException => true, // connection closed while reading the body
            IOException { InnerException: SocketException } => true,
            _ => false
        };
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
