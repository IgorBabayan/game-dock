using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using GameDock.Services.GitHub;
using GameDock.Tests.Support;

namespace GameDock.Tests.Services;

public class GitHubReleaseClientTests : IDisposable
{
    private static readonly Regex InstallerPattern = new(@"^App-Setup-.*\.exe$");
    private static readonly TimeSpan[] NoDelays = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero];

    private readonly StubHttpMessageHandler _api = new();
    private readonly StubHttpMessageHandler _downloads = new();
    private readonly GitHubReleaseClient _client;
    private readonly TempDirectory _temp = new();

    public GitHubReleaseClientTests()
    {
        _client = new GitHubReleaseClient(_api, _downloads, NoDelays);
    }

    public void Dispose()
    {
        _client.Dispose();
        _temp.Dispose();
    }

    private static string ReleaseJson(
        string tag = "v1.2.3",
        bool draft = false,
        bool prerelease = false,
        string assets = """
            [
              { "name": "Source.zip", "browser_download_url": "https://example.com/Source.zip", "size": 1, "digest": null },
              { "name": "App-Setup-1.2.3.exe", "browser_download_url": "https://example.com/App-Setup-1.2.3.exe", "size": 1234, "digest": "sha256:ABCDEF" }
            ]
            """) => $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://github.com/owner/repo/releases/tag/{{tag}}",
          "draft": {{(draft ? "true" : "false")}},
          "prerelease": {{(prerelease ? "true" : "false")}},
          "published_at": "2026-11-01T10:00:00Z",
          "assets": {{assets}}
        }
        """;

    // ===== GetLatestReleaseAsync =====

    [Fact]
    public async Task GetLatestRelease_ParsesReleaseAndSelectsInstaller()
    {
        _api.Enqueue(HttpStatusCode.OK, ReleaseJson());

        var release = await _client.GetLatestReleaseAsync("owner", "repo", InstallerPattern);

        Assert.NotNull(release);
        Assert.Equal(new Version(1, 2, 3), release.Version);
        Assert.Equal("v1.2.3", release.TagName);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero), release.PublishedAt);
        Assert.Equal("https://github.com/owner/repo/releases/tag/v1.2.3", release.PageUrl?.AbsoluteUri);

        var installer = Assert.IsType<ReleaseAsset>(release.Installer);
        Assert.Equal("App-Setup-1.2.3.exe", installer.Name);
        Assert.Equal("https://example.com/App-Setup-1.2.3.exe", installer.DownloadUrl.AbsoluteUri);
        Assert.Equal(1234, installer.Size);
        Assert.Equal("ABCDEF", installer.Sha256);
    }

    [Fact]
    public async Task GetLatestRelease_SendsGitHubApiHeaders()
    {
        _api.Enqueue(HttpStatusCode.OK, ReleaseJson());

        await _client.GetLatestReleaseAsync("owner", "repo", InstallerPattern);

        var request = Assert.Single(_api.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.github.com/repos/owner/repo/releases/latest", request.RequestUri?.AbsoluteUri);
        Assert.Contains(request.Headers.Accept, h => h.MediaType == "application/vnd.github+json");
        Assert.True(request.Headers.Contains("X-GitHub-Api-Version"));
        Assert.Contains(request.Headers.UserAgent, p => p.Product?.Name == "GameDock");
    }

    [Fact]
    public async Task GetLatestRelease_NoMatchingAsset_ReturnsReleaseWithoutInstaller()
    {
        _api.Enqueue(HttpStatusCode.OK, ReleaseJson(assets: """
            [ { "name": "App-1.2.3-linux-x64.tar.gz", "browser_download_url": "https://example.com/a.tar.gz", "size": 5 } ]
            """));

        var release = await _client.GetLatestReleaseAsync("owner", "repo", InstallerPattern);

        Assert.NotNull(release);
        Assert.Null(release.Installer);
    }

    [Fact]
    public async Task GetLatestRelease_AssetWithoutDigestOrValidUrl_IsHandled()
    {
        _api.Enqueue(HttpStatusCode.OK, ReleaseJson(assets: """
            [ { "name": "App-Setup-1.exe", "browser_download_url": "https://example.com/App-Setup-1.exe", "size": 5, "digest": "md5:123" } ]
            """));
        _api.Enqueue(HttpStatusCode.OK, ReleaseJson(assets: """
            [ { "name": "App-Setup-1.exe", "browser_download_url": "not a url", "size": 5 } ]
            """));

        var withoutSha = await _client.GetLatestReleaseAsync("owner", "repo", InstallerPattern);
        var badUrl = await _client.GetLatestReleaseAsync("owner", "repo", InstallerPattern);

        Assert.Null(withoutSha?.Installer?.Sha256);
        Assert.NotNull(withoutSha?.Installer);
        Assert.Null(badUrl?.Installer);
    }

    [Fact]
    public async Task GetLatestRelease_NotFound_ReturnsNull()
    {
        _api.Enqueue(HttpStatusCode.NotFound);

        Assert.Null(await _client.GetLatestReleaseAsync("owner", "repo", InstallerPattern));
    }

    [Theory]
    [InlineData("v1.2.3", true, false)]
    [InlineData("v1.2.3", false, true)]
    [InlineData("nightly", false, false)]
    public async Task GetLatestRelease_DraftPrereleaseOrInvalidTag_ReturnsNull(string tag, bool draft, bool prerelease)
    {
        _api.Enqueue(HttpStatusCode.OK, ReleaseJson(tag, draft, prerelease));

        Assert.Null(await _client.GetLatestReleaseAsync("owner", "repo", InstallerPattern));
    }

    [Fact]
    public async Task GetLatestRelease_RateLimited_ThrowsWithResetTime()
    {
        var reset = DateTimeOffset.UtcNow.AddMinutes(20);
        _api.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
            response.Headers.Add("x-ratelimit-remaining", "0");
            response.Headers.Add("x-ratelimit-reset", reset.ToUnixTimeSeconds().ToString());
            return response;
        });

        var ex = await Assert.ThrowsAsync<GitHubRateLimitException>(
            () => _client.GetLatestReleaseAsync("owner", "repo", InstallerPattern));

        Assert.Equal(reset.ToUnixTimeSeconds(), ex.ResetsAt?.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task GetLatestRelease_TooManyRequests_ThrowsRateLimit()
    {
        _api.Enqueue(HttpStatusCode.TooManyRequests);

        var ex = await Assert.ThrowsAsync<GitHubRateLimitException>(
            () => _client.GetLatestReleaseAsync("owner", "repo", InstallerPattern));

        Assert.Null(ex.ResetsAt);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetLatestRelease_OtherErrors_ThrowHttpRequestException(HttpStatusCode status)
    {
        _api.Enqueue(status);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => _client.GetLatestReleaseAsync("owner", "repo", InstallerPattern));

        Assert.IsNotType<GitHubRateLimitException>(ex);
        Assert.Equal(status, ex.StatusCode);
    }

    [Fact]
    public async Task GetLatestRelease_ValidatesArguments()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _client.GetLatestReleaseAsync("", "repo", InstallerPattern));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _client.GetLatestReleaseAsync("owner", " ", InstallerPattern));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetLatestReleaseAsync("owner", "repo", null!));
        Assert.Empty(_api.Requests);
    }

    [Fact]
    public async Task GetLatestRelease_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _client.GetLatestReleaseAsync("owner", "repo", InstallerPattern, cts.Token));
    }

    // ===== DownloadAsync =====

    private static byte[] Payload(int length)
    {
        var bytes = new byte[length];
        new Random(length).NextBytes(bytes);
        return bytes;
    }

    private static ReleaseAsset Asset(byte[] payload, bool withSha = true, long? size = null) => new(
        "App-Setup-1.2.3.exe",
        new Uri("https://example.com/App-Setup-1.2.3.exe"),
        size ?? payload.Length,
        withSha ? Convert.ToHexStringLower(SHA256.HashData(payload)) : null);

    [Fact]
    public async Task Download_WritesVerifiedFileAndReportsFinalProgress()
    {
        var payload = Payload(300_000);
        _downloads.EnqueueBytes(HttpStatusCode.OK, payload);
        var progress = new RecordingProgress();

        var path = await _client.DownloadAsync(Asset(payload), _temp.Combine("dl"), progress);

        Assert.Equal(_temp.Combine("dl", "App-Setup-1.2.3.exe"), path);
        Assert.Equal(payload, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(path + ".part"));
        Assert.Equal(new DownloadProgress(payload.Length, payload.Length), progress.Reports[^1]);
        Assert.Equal(0, progress.Reports[0].BytesReceived);
    }

    [Fact]
    public async Task Download_ShaIsCheckedCaseInsensitively()
    {
        var payload = Payload(1000);
        _downloads.EnqueueBytes(HttpStatusCode.OK, payload);
        var asset = Asset(payload) with { Sha256 = Convert.ToHexString(SHA256.HashData(payload)) };

        var path = await _client.DownloadAsync(asset, _temp.Path, progress: null);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Download_WithoutShaOrSize_StillSucceeds()
    {
        var payload = Payload(1000);
        _downloads.EnqueueBytes(HttpStatusCode.OK, payload);
        var progress = new RecordingProgress();

        var path = await _client.DownloadAsync(Asset(payload, withSha: false, size: 0), _temp.Path, progress);

        Assert.Equal(payload, await File.ReadAllBytesAsync(path));
        Assert.Equal(payload.Length, progress.Reports[^1].TotalBytes);
    }

    [Fact]
    public async Task Download_ShaMismatch_ThrowsAndLeavesNoFiles()
    {
        var payload = Payload(1000);
        _downloads.EnqueueBytes(HttpStatusCode.OK, payload);
        var asset = Asset(payload) with { Sha256 = new string('0', 64) };

        await Assert.ThrowsAsync<InvalidDataException>(() => _client.DownloadAsync(asset, _temp.Path, null));

        Assert.Empty(Directory.GetFiles(_temp.Path));
    }

    [Fact]
    public async Task Download_SizeMismatch_ThrowsAndLeavesNoFiles()
    {
        var payload = Payload(1000);
        _downloads.EnqueueBytes(HttpStatusCode.OK, payload);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _client.DownloadAsync(Asset(payload, withSha: false, size: 999), _temp.Path, null));

        Assert.Empty(Directory.GetFiles(_temp.Path));
    }

    [Fact]
    public async Task Download_RetriesTransientServerErrors()
    {
        var payload = Payload(1000);
        _downloads
            .Enqueue(HttpStatusCode.BadGateway)
            .EnqueueException(new HttpRequestException("connection refused"))
            .Enqueue(HttpStatusCode.ServiceUnavailable)
            .EnqueueBytes(HttpStatusCode.OK, payload);

        var path = await _client.DownloadAsync(Asset(payload), _temp.Path, null);

        Assert.Equal(payload, await File.ReadAllBytesAsync(path));
        Assert.Equal(4, _downloads.Requests.Count);
    }

    [Fact]
    public async Task Download_GivesUpAfterLastRetry()
    {
        var payload = Payload(10);
        for (var i = 0; i < NoDelays.Length + 1; i++)
            _downloads.Enqueue(HttpStatusCode.ServiceUnavailable);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _client.DownloadAsync(Asset(payload), _temp.Path, null));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        Assert.Contains("not responding", ex.Message);
        Assert.Equal(NoDelays.Length + 1, _downloads.Requests.Count);
        Assert.Empty(Directory.GetFiles(_temp.Path));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Download_ClientErrors_AreNotRetried(HttpStatusCode status)
    {
        _downloads.Enqueue(status);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _client.DownloadAsync(Asset(Payload(10)), _temp.Path, null));

        Assert.Equal(status, ex.StatusCode);
        Assert.Single(_downloads.Requests);
    }

    [Fact]
    public async Task Download_ResumesWithRangeRequestAfterDroppedConnection()
    {
        var payload = Payload(200_000);
        const int firstPart = 70_000;

        _downloads
            .Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new FailingStream(payload.AsMemory(0, firstPart).ToArray()))
            })
            .Enqueue(request =>
            {
                var from = (int)request.Headers.Range!.Ranges.Single().From!.Value;
                return new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(payload, from, payload.Length - from)
                };
            });

        var path = await _client.DownloadAsync(Asset(payload), _temp.Path, null);

        Assert.Equal(payload, await File.ReadAllBytesAsync(path));
        Assert.Null(_downloads.Requests[0].Headers.Range);
        Assert.Equal(firstPart, _downloads.Requests[1].Headers.Range?.Ranges.Single().From);
    }

    [Fact]
    public async Task Download_ServerIgnoringRange_StartsOver()
    {
        var payload = Payload(50_000);
        _downloads
            .Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new FailingStream(payload.AsMemory(0, 10_000).ToArray()))
            })
            .EnqueueBytes(HttpStatusCode.OK, payload); // 200 with the whole file instead of 206

        var path = await _client.DownloadAsync(Asset(payload), _temp.Path, null);

        Assert.Equal(payload, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Download_IgnoresLeftoverPartialFile()
    {
        var payload = Payload(1000);
        await File.WriteAllBytesAsync(_temp.Combine("App-Setup-1.2.3.exe.part"), Payload(400));
        _downloads.EnqueueBytes(HttpStatusCode.OK, payload);

        var path = await _client.DownloadAsync(Asset(payload), _temp.Path, null);

        Assert.Equal(payload, await File.ReadAllBytesAsync(path));
        Assert.Null(Assert.Single(_downloads.Requests).Headers.Range);
    }

    [Fact]
    public async Task Download_StripsDirectoriesFromAssetName()
    {
        var payload = Payload(100);
        _downloads.EnqueueBytes(HttpStatusCode.OK, payload);
        var asset = Asset(payload) with { Name = "../../evil.exe" };

        var path = await _client.DownloadAsync(asset, _temp.Path, null);

        Assert.Equal(_temp.Combine("evil.exe"), path);
    }

    [Fact]
    public async Task Download_Cancelled_IsNotRetriedAndCleansUp()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _client.DownloadAsync(Asset(Payload(10)), _temp.Path, null, cts.Token));

        Assert.Empty(Directory.GetFiles(_temp.Path));
    }

    [Fact]
    public async Task Download_ValidatesArguments()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.DownloadAsync(null!, _temp.Path, null));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _client.DownloadAsync(Asset(Payload(1)), " ", null));
    }

    [Fact]
    public void Constructor_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new GitHubReleaseClient(null!, _downloads, NoDelays));
        Assert.Throws<ArgumentNullException>(() => new GitHubReleaseClient(_api, null!, NoDelays));
        Assert.Throws<ArgumentNullException>(() => new GitHubReleaseClient(_api, _downloads, null!));
    }

    [Fact]
    public void DefaultConstructor_CreatesUsableClient()
    {
        using var client = new GitHubReleaseClient();
        Assert.NotNull(client);
    }

    /// <summary>Synchronous <see cref="IProgress{T}"/> (Progress&lt;T&gt; posts to the thread pool).</summary>
    private sealed class RecordingProgress : IProgress<DownloadProgress>
    {
        public List<DownloadProgress> Reports { get; } = [];

        public void Report(DownloadProgress value) => Reports.Add(value);
    }

    /// <summary>Returns <paramref name="data"/>, then fails like a dropped connection.</summary>
    private sealed class FailingStream(byte[] data) : MemoryStream(data)
    {
        public override int Read(byte[] buffer, int offset, int count) =>
            Position < Length ? base.Read(buffer, offset, count) : throw new HttpIOException(HttpRequestError.ResponseEnded);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Position < Length
                ? base.ReadAsync(buffer, cancellationToken)
                : ValueTask.FromException<int>(new HttpIOException(HttpRequestError.ResponseEnded));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }
}
