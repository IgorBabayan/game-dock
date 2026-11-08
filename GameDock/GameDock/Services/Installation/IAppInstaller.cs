using GameDock.Services.Catalog;
using GameDock.Services.GitHub;

namespace GameDock.Services.Installation;

/// <summary>Installs, updates, removes and starts managed apps. Operations run one at a time.</summary>
public interface IAppInstaller
{
    /// <summary>
    /// Downloads the installer of <paramref name="release"/> and runs it silently.
    /// Used for both a fresh install and an update (the installer replaces the previous version in place).
    /// </summary>
    /// <param name="current">Current installation for an update, <c>null</c> for a fresh install.</param>
    /// <param name="onStage">Reports "Downloading" / "Installing" so the UI can switch its status text.</param>
    Task InstallAsync(
        ManagedApp app,
        ReleaseInfo release,
        InstalledApp? current,
        IProgress<DownloadProgress>? progress,
        Action<InstallStage>? onStage,
        CancellationToken cancellationToken = default);

    /// <summary>Runs the app's uninstaller silently and waits until it has finished.</summary>
    Task UninstallAsync(ManagedApp app, InstalledApp installed, CancellationToken cancellationToken = default);

    /// <summary>Starts the app.</summary>
    void Launch(ManagedApp app, InstalledApp installed);

    /// <summary>True if the app's executable from <paramref name="installed"/> is running.</summary>
    bool IsRunning(ManagedApp app, InstalledApp installed);

    /// <summary>Removes downloaded installers. Never throws.</summary>
    void CleanupDownloads();
}

public enum InstallStage
{
    Waiting,
    Downloading,
    Installing,
    Removing
}

/// <summary>An installer or uninstaller ended with an error.</summary>
public sealed class InstallerFailedException(string message) : Exception(message);
