using System.Text.RegularExpressions;
using GameDock.Services.GitHub;
using GameDock.Services.Platform;
using GameDock.Services.Updates;

namespace GameDock.Tests.Services;

public class GitHubUpdateServiceTests
{
    private readonly FakeReleaseClient _client = new();
    private readonly GitHubUpdateService _service;

    public GitHubUpdateServiceTests()
    {
        _service = new GitHubUpdateService(_client);
    }

    private static ReleaseAsset Installer => new("GameDock-Setup-9.0.0.exe", new Uri("https://example.com/i.exe"), 10, null);

    private static ReleaseInfo Release(Version version, ReleaseAsset? installer) =>
        new(version, $"v{version}", DateTimeOffset.UtcNow, null, installer);

    [Fact]
    public void CurrentVersion_IsNormalized()
    {
        Assert.Equal(VersionHelper.Normalize(_service.CurrentVersion), _service.CurrentVersion);
    }

    [Fact]
    public void Platform_Flags()
    {
        Assert.Equal(!OperatingSystem.IsWindows(), _service.CanNotifyOnly);
        // The test host is never an Inno Setup install.
        Assert.False(_service.IsInstalled);
    }

    [Fact]
    public async Task CheckForUpdate_NewerReleaseWithInstaller_ReturnsIt()
    {
        var release = Release(new Version(int.MaxValue, 0, 0), Installer);
        _client.Latest = release;

        Assert.Same(release, await _service.CheckForUpdateAsync());
        Assert.Equal((GitHubUpdateService.Owner, GitHubUpdateService.Repository), (_client.Owner, _client.Repository));
    }

    [Fact]
    public async Task CheckForUpdate_SameVersion_ReturnsNull()
    {
        _client.Latest = Release(_service.CurrentVersion, Installer);

        Assert.Null(await _service.CheckForUpdateAsync());
    }

    [Fact]
    public async Task CheckForUpdate_OlderVersion_ReturnsNull()
    {
        _client.Latest = Release(new Version(0, 0, 0), Installer);

        Assert.Null(await _service.CheckForUpdateAsync());
    }

    [Fact]
    public async Task CheckForUpdate_NewerReleaseWithoutInstaller_ReturnsNull()
    {
        _client.Latest = Release(new Version(int.MaxValue, 0, 0), installer: null);

        Assert.Null(await _service.CheckForUpdateAsync());
    }

    [Fact]
    public async Task CheckForUpdate_NoRelease_ReturnsNull()
    {
        _client.Latest = null;

        Assert.Null(await _service.CheckForUpdateAsync());
    }

    [Fact]
    public async Task CheckForUpdate_UsesThisPlatformsInstallerPattern()
    {
        await _service.CheckForUpdateAsync();

        var pattern = Assert.IsType<Regex>(_client.Pattern);
        if (OperatingSystem.IsWindows())
        {
            Assert.Matches(pattern, "GameDock-Setup-0.1.12.exe");
            Assert.DoesNotMatch(pattern, "GameDock-0.1.12-linux-x64.tar.gz");
        }
        else
        {
            Assert.Matches(pattern, $"GameDock-0.1.12-{PlatformInfo.RuntimeId}.tar.gz");
            Assert.Matches(pattern, $"GameDock-0.1.12-{PlatformInfo.RuntimeId}.zip");
            Assert.DoesNotMatch(pattern, "GameDock-Setup-0.1.12.exe");
        }
    }

    [Fact]
    public async Task CheckForUpdate_PropagatesRateLimit()
    {
        _client.Error = new GitHubRateLimitException(null);

        await Assert.ThrowsAsync<GitHubRateLimitException>(() => _service.CheckForUpdateAsync());
    }

    [Fact]
    public async Task Download_DelegatesToClientWithTheInstaller()
    {
        var path = await _service.DownloadAsync(Release(new Version(9, 0, 0), Installer), null);

        Assert.Equal("downloaded", path);
        Assert.Equal(Installer, _client.DownloadedAsset);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "GameDock", "Updates"), _client.DownloadFolder);
    }

    [Fact]
    public async Task Download_WithoutInstaller_Throws()
    {
        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => _service.DownloadAsync(Release(new Version(9, 0, 0), null), null));

        Assert.Contains(PlatformInfo.InstallerKind, ex.Message);
        Assert.Null(_client.DownloadedAsset);
    }

    [Fact]
    public async Task Download_Null_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.DownloadAsync(null!, null));
    }

    [Fact]
    public void LaunchInstaller_EmptyPath_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => _service.LaunchInstaller(" "));
    }

    [Fact]
    public void OpenReleasePage_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _service.OpenReleasePage(null!));
    }

    private sealed class FakeReleaseClient : IGitHubReleaseClient
    {
        public ReleaseInfo? Latest { get; set; }
        public Exception? Error { get; set; }
        public string? Owner { get; private set; }
        public string? Repository { get; private set; }
        public Regex? Pattern { get; private set; }
        public ReleaseAsset? DownloadedAsset { get; private set; }
        public string? DownloadFolder { get; private set; }

        public Task<ReleaseInfo?> GetLatestReleaseAsync(
            string owner, string repository, Regex installerPattern, CancellationToken cancellationToken = default)
        {
            (Owner, Repository, Pattern) = (owner, repository, installerPattern);
            return Error is null ? Task.FromResult(Latest) : Task.FromException<ReleaseInfo?>(Error);
        }

        public Task<string> DownloadAsync(
            ReleaseAsset asset, string folder, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken = default)
        {
            (DownloadedAsset, DownloadFolder) = (asset, folder);
            return Task.FromResult("downloaded");
        }
    }
}
