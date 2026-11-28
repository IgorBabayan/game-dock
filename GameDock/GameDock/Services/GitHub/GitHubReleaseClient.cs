using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GameDock.Services.GitHub;

/// <summary>Anonymous client for <c>api.github.com/repos/{owner}/{repo}/releases/latest</c>.</summary>
public sealed class GitHubReleaseClient : IGitHubReleaseClient, IDisposable
{
    /// <summary>Pauses between download attempts; one more attempt than there are delays.</summary>
    private static readonly TimeSpan[] DefaultDownloadRetryDelays =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

    /// <summary>
    /// Download progress is reported at most this often. Network reads return every few KB, and every report is
    /// marshalled to the UI thread and re-formats texts: unthrottled that is thousands of UI updates per installer.
    /// </summary>
    private const long ProgressIntervalMs = 100;

    private const int BufferSize = 81920;

    /// <summary>API calls: small JSON documents, compressed on the wire.</summary>
    private readonly HttpClient _api;

    /// <summary>
    /// Asset downloads: separate client without automatic decompression, so HTTP range requests (resume) always
    /// address the raw bytes of the file.
    /// </summary>
    private readonly HttpClient _downloads;

    private readonly TimeSpan[] _downloadRetryDelays;

    public GitHubReleaseClient()
        : this(CreateHandler(DecompressionMethods.All), CreateHandler(DecompressionMethods.None), DefaultDownloadRetryDelays)
    {
    }

    /// <summary>Tests: custom transports (no network) and short retry pauses.</summary>
    internal GitHubReleaseClient(HttpMessageHandler apiHandler, HttpMessageHandler downloadHandler, TimeSpan[] downloadRetryDelays)
    {
        ArgumentNullException.ThrowIfNull(apiHandler);
        ArgumentNullException.ThrowIfNull(downloadHandler);
        ArgumentNullException.ThrowIfNull(downloadRetryDelays);

        _downloadRetryDelays = downloadRetryDelays;

        var version = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
        // GitHub rejects API requests without a User-Agent.
        var userAgent = new ProductInfoHeaderValue("GameDock", VersionHelper.Normalize(version).ToString());

        // Timeouts are controlled per call with cancellation tokens (downloads may take a while).
        _api = new HttpClient(apiHandler) { Timeout = Timeout.InfiniteTimeSpan };
        _api.DefaultRequestHeaders.UserAgent.Add(userAgent);

        // No automatic decompression, so HTTP range requests (resume) always address the raw bytes of the file.
        _downloads = new HttpClient(downloadHandler) { Timeout = Timeout.InfiniteTimeSpan };
        _downloads.DefaultRequestHeaders.UserAgent.Add(userAgent);
    }

    private static SocketsHttpHandler CreateHandler(DecompressionMethods decompression) => new()
    {
        AutomaticDecompression = decompression,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        ConnectTimeout = TimeSpan.FromSeconds(15)
    };

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

        // Stream the body straight into the deserializer instead of buffering it first.
        using var response = await _api
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        // No published release yet.
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (IsRateLimited(response, out var resetsAt))
            throw new GitHubRateLimitException(resetsAt);

        response.EnsureSuccessStatusCode();

        await using var json = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        // Source-generated metadata: no reflection-based serializer warm-up on the first check.
        var release = await JsonSerializer
            .DeserializeAsync(json, GitHubJsonContext.Default.ReleaseDto, cancellationToken)
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

        // SHA-256 is computed while the bytes arrive, so the file is not read back from disk to be verified.
        using var hash = asset.Sha256 is null ? null : new Sha256Tracker();

        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await DownloadToFileAsync(asset, partialPath, hash, progress, cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (Exception ex) when (attempt < _downloadRetryDelays.Length + 1 &&
                                           IsTransientDownloadError(ex, cancellationToken))
                {
                    // GitHub's release CDN regularly answers 502/503/504 for a short while (especially right
                    // after an asset is published) or drops the connection: wait and continue where we stopped.
                    Debug.WriteLine($"Download attempt {attempt} of {asset.Name} failed: {ex.Message}");
                    await Task.Delay(_downloadRetryDelays[attempt - 1], cancellationToken).ConfigureAwait(false);
                }
            }

            Verify(partialPath, asset, hash);

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
        Sha256Tracker? hash,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var existing = File.Exists(path) ? new FileInfo(path).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
        if (existing > 0)
            request.Headers.Range = new RangeHeaderValue(existing, null);

        using var response = await _downloads
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        // The previous attempt already received the whole file.
        if (existing > 0 && existing == asset.Size &&
            response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            if (hash is not null && hash.Length != existing)
                await hash.RebuildAsync(path, cancellationToken).ConfigureAwait(false);
            return;
        }

        EnsureDownloadSucceeded(response);

        // Server ignored the range (200 instead of 206): start over.
        var resume = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        var received = resume ? existing : 0;
        var total = asset.Size > 0
            ? asset.Size
            : response.Content.Headers.ContentLength is { } length ? length + received : 0;

        if (hash is not null)
        {
            if (!resume)
                hash.Reset();
            else if (hash.Length != received)
                // Rare: the bytes on disk and the running hash got out of step (e.g. a write failed halfway).
                await hash.RebuildAsync(path, cancellationToken).ConfigureAwait(false);
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(
            path, resume ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            var lastReport = Environment.TickCount64;
            progress?.Report(new DownloadProgress(received, total));

            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                hash?.Append(buffer.AsSpan(0, read));
                received += read;

                if (progress is not null)
                {
                    var now = Environment.TickCount64;
                    if (now - lastReport >= ProgressIntervalMs)
                    {
                        lastReport = now;
                        progress.Report(new DownloadProgress(received, total));
                    }
                }
            }

            // Always end on the exact final value.
            progress?.Report(new DownloadProgress(received, total));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
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

    public void Dispose()
    {
        _api.Dispose();
        _downloads.Dispose();
    }

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

    private static void Verify(string path, ReleaseAsset asset, Sha256Tracker? hash)
    {
        var length = new FileInfo(path).Length;
        if (asset.Size > 0 && length != asset.Size)
            throw new InvalidDataException($"Downloaded file has {length} bytes, expected {asset.Size}.");

        if (asset.Sha256 is null || hash is null)
            return;

        if (hash.Length != length ||
            !string.Equals(hash.GetHashAndReset(), asset.Sha256, StringComparison.OrdinalIgnoreCase))
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

    /// <summary>Running SHA-256 of the bytes written to the partial file.</summary>
    private sealed class Sha256Tracker : IDisposable
    {
        private IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        /// <summary>Number of bytes hashed so far.</summary>
        public long Length { get; private set; }

        public void Append(ReadOnlySpan<byte> data)
        {
            _hash.AppendData(data);
            Length += data.Length;
        }

        public void Reset()
        {
            _hash.Dispose();
            _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Length = 0;
        }

        /// <summary>Re-hashes what is on disk (only needed when a resumed download lost track).</summary>
        public async Task RebuildAsync(string path, CancellationToken cancellationToken)
        {
            Reset();

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                int read;
                while ((read = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false)) > 0)
                    Append(buffer.AsSpan(0, read));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>Lower-case hex SHA-256 of everything appended.</summary>
        public string GetHashAndReset()
        {
            var hex = Convert.ToHexStringLower(_hash.GetHashAndReset());
            Length = 0;
            return hex;
        }

        public void Dispose() => _hash.Dispose();
    }
}

// ===== GitHub API payload (only the fields we use) =====

internal sealed record ReleaseDto(
    string? TagName,
    string? HtmlUrl,
    bool Draft,
    bool Prerelease,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<AssetDto>? Assets);

internal sealed record AssetDto(
    string? Name,
    string? BrowserDownloadUrl,
    long Size,
    string? Digest);

/// <summary>Compile-time JSON metadata for the GitHub payload (snake_case properties).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ReleaseDto))]
internal sealed partial class GitHubJsonContext : JsonSerializerContext;
