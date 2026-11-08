using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using GameDock.Services.GitHub;

namespace GameDock.Services.Updates;

/// <summary>
/// Uses the latest published GitHub Release of <c>IgorBabayan/game-dock</c>
/// (created by <c>.github/workflows/release-installer.yml</c>) as the update source.
/// </summary>
public sealed class GitHubUpdateService : IUpdateService
{
    public const string Owner = "IgorBabayan";
    public const string Repository = "game-dock";

    /// <summary>Inno Setup always names the uninstaller of the first install like this.</summary>
    private const string UninstallerFileName = "unins000.exe";

    /// <summary>GameDock-Setup-0.1.12.exe</summary>
    private static readonly Regex InstallerPattern = new(
        @"^GameDock-Setup.*\.exe$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly string DownloadFolder =
        Path.Combine(Path.GetTempPath(), "GameDock", "Updates");

    private readonly IGitHubReleaseClient _releaseClient;

    public GitHubUpdateService(IGitHubReleaseClient releaseClient)
    {
        _releaseClient = releaseClient;

        var version = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
        CurrentVersion = VersionHelper.Normalize(version);
        IsInstalled = File.Exists(Path.Combine(AppContext.BaseDirectory, UninstallerFileName));
    }

    public Version CurrentVersion { get; }

    public bool IsInstalled { get; }

    public async Task<ReleaseInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        var release = await _releaseClient
            .GetLatestReleaseAsync(Owner, Repository, InstallerPattern, cancellationToken)
            .ConfigureAwait(false);

        return release is { Installer: not null } && release.Version > CurrentVersion ? release : null;
    }

    public Task<string> DownloadAsync(
        ReleaseInfo update,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var installer = update.Installer
                        ?? throw new InvalidDataException("The release has no Windows installer.");

        return _releaseClient.DownloadAsync(installer, DownloadFolder, progress, cancellationToken);
    }

    public void LaunchInstaller(string installerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);

        var logPath = Path.Combine(DownloadFolder, "install.log");

        // /VERYSILENT              no wizard and no progress window
        // /CLOSEAPPLICATIONS       close this app if it is still running when files are replaced
        // /NORESTARTAPPLICATIONS   do not let Restart Manager start it again...
        // /RELAUNCH=1              ...because the installer script starts it itself after installation
        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /RELAUNCH=1 /LOG=\"{logPath}\"",
            // Shell execute lets Windows show the UAC prompt if the app was installed for all users.
            UseShellExecute = true,
            WorkingDirectory = DownloadFolder
        });
    }

    public void CleanupDownloads()
    {
        try
        {
            if (Directory.Exists(DownloadFolder))
                Directory.Delete(DownloadFolder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file may still be locked by a finishing installer; it will be removed next time.
            Debug.WriteLine($"Update cleanup failed: {ex.Message}");
        }
    }
}
