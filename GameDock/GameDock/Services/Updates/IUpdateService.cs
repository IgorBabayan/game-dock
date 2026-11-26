using GameDock.Services.GitHub;

namespace GameDock.Services.Updates;

/// <summary>Checks GitHub Releases of App Launcher itself for a newer version and installs it.</summary>
public interface IUpdateService
{
    /// <summary>Version of the running application (Major.Minor.Build).</summary>
    Version CurrentVersion { get; }

    /// <summary>
    /// True when the app runs from an installed copy (the Inno Setup uninstaller sits next to the exe).
    /// Updates are never applied to development builds or unpacked copies.
    /// </summary>
    bool IsInstalled { get; }

    /// <summary>
    /// True where updates cannot be installed automatically (Linux / macOS portable builds): a newer release is
    /// only announced, with a link to its download page.
    /// </summary>
    bool CanNotifyOnly { get; }

    /// <summary>Opens the web page of <paramref name="update"/> (or the latest release) in the browser.</summary>
    void OpenReleasePage(ReleaseInfo update);

    /// <summary>Returns the latest release if it is newer than <see cref="CurrentVersion"/>, otherwise <c>null</c>.</summary>
    Task<ReleaseInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default);

    /// <summary>Downloads (and verifies) the installer, returning the local file path.</summary>
    Task<string> DownloadAsync(ReleaseInfo update, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the installer silently. The installer closes this app, installs the update and starts the app again,
    /// so the caller must shut the application down right after this returns.
    /// </summary>
    void LaunchInstaller(string installerPath);

    /// <summary>Removes installers downloaded by previous updates. Never throws.</summary>
    void CleanupDownloads();
}
