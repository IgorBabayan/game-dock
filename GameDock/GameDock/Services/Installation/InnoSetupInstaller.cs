using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using GameDock.Services.Catalog;
using GameDock.Services.GitHub;

namespace GameDock.Services.Installation;

/// <summary>
/// Drives the apps' Inno Setup installers and uninstallers in silent mode.
/// Only one operation runs at a time; the others wait in line.
/// </summary>
public sealed class InnoSetupInstaller(
    IGitHubReleaseClient releaseClient,
    IInstalledAppRegistry registry) : IAppInstaller, IDisposable
{
    /// <summary>ERROR_CANCELLED: the user answered "No" to the UAC prompt.</summary>
    private const int ErrorCancelled = 1223;

    private static readonly TimeSpan UninstallTimeout = TimeSpan.FromMinutes(3);

    private static readonly string DownloadRoot =
        Path.Combine(Path.GetTempPath(), "GameDock", "Apps");

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task InstallAsync(
        ManagedApp app,
        ReleaseInfo release,
        InstalledApp? current,
        IProgress<DownloadProgress>? progress,
        Action<InstallStage>? onStage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(release);

        var installer = release.Installer
                        ?? throw new InstallerFailedException(
                            $"Release {release.TagName} of {app.Name} has no Windows installer.");

        await EnterAsync(onStage, cancellationToken).ConfigureAwait(false);
        try
        {
            onStage?.Invoke(InstallStage.Downloading);
            var folder = Path.Combine(DownloadRoot, app.Id);
            var installerPath = await releaseClient
                .DownloadAsync(installer, folder, progress, cancellationToken)
                .ConfigureAwait(false);

            // Past this point the installer runs to completion: cancelling halfway would leave a broken install.
            onStage?.Invoke(InstallStage.Installing);

            // /VERYSILENT              no wizard and no progress window
            // /SUPPRESSMSGBOXES        answer message boxes with their defaults
            // /CLOSEAPPLICATIONS       close the app if it is running while its files are replaced
            // /NORESTARTAPPLICATIONS   do not start it again afterwards (the user opens it from Game Dock)
            // /ALLUSERS                keep an existing all-users installation in place (asks for admin rights);
            //                          otherwise the installers default to a per-user install (no UAC prompt)
            var mode = current is { IsAllUsers: true } ? " /ALLUSERS" : string.Empty;
            var logPath = Path.Combine(folder, "install.log");
            var arguments =
                $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CLOSEAPPLICATIONS /NORESTARTAPPLICATIONS{mode} /LOG=\"{logPath}\"";

            var exitCode = await RunAsync(installerPath, arguments, folder).ConfigureAwait(false);
            if (exitCode != 0)
                throw new InstallerFailedException(
                    $"The {app.Name} installer failed (exit code {exitCode}). See {logPath}.");

            if (registry.Find(app) is null)
                throw new InstallerFailedException($"{app.Name} was not found after installation. See {logPath}.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UninstallAsync(ManagedApp app, InstalledApp installed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(installed);

        var uninstaller = installed.UninstallerPath;
        if (string.IsNullOrWhiteSpace(uninstaller) || !File.Exists(uninstaller))
            throw new InstallerFailedException($"The {app.Name} uninstaller was not found.");

        await EnterAsync(null, cancellationToken).ConfigureAwait(false);
        try
        {
            var exitCode = await RunAsync(
                    uninstaller,
                    "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
                    Path.GetTempPath())
                .ConfigureAwait(false);

            if (exitCode != 0)
                throw new InstallerFailedException($"The {app.Name} uninstaller failed (exit code {exitCode}).");

            // Inno Setup's uninstaller hands the work over to a copy of itself in %TEMP%, so the process we
            // started can exit before the files are gone. Wait until the app is unregistered.
            var deadline = DateTime.UtcNow + UninstallTimeout;
            while (registry.Find(app) is not null)
            {
                if (DateTime.UtcNow > deadline)
                    throw new InstallerFailedException($"Removing {app.Name} did not finish in time.");

                await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Launch(ManagedApp app, InstalledApp installed)
    {
        var exe = installed.GetExePath(app.ExeName);
        if (!File.Exists(exe))
            throw new FileNotFoundException($"{app.Name} executable was not found.", exe);

        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = installed.InstallFolder,
            UseShellExecute = true
        })?.Dispose();
    }

    public bool IsRunning(ManagedApp app, InstalledApp installed)
    {
        var exe = Path.GetFullPath(installed.GetExePath(app.ExeName));
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(app.ExeName));
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (path is null || string.Equals(Path.GetFullPath(path), exe, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    // Elevated or already exited: the name matches, assume it is ours.
                    return !process.HasExited;
                }
            }

            return false;
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    public void CleanupDownloads()
    {
        try
        {
            if (Directory.Exists(DownloadRoot))
                Directory.Delete(DownloadRoot, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file may still be locked by a finishing installer; it will be removed next time.
            Debug.WriteLine($"Installer cleanup failed: {ex.Message}");
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task EnterAsync(Action<InstallStage>? onStage, CancellationToken cancellationToken)
    {
        if (await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return;

        // Another app is being installed or removed: wait for it.
        onStage?.Invoke(InstallStage.Waiting);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> RunAsync(string fileName, string arguments, string workingDirectory)
    {
        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                // Shell execute lets Windows show the UAC prompt for all-users installations.
                UseShellExecute = true
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            throw new InstallerFailedException("Administrator permission was not granted.");
        }

        if (process is null)
            throw new InstallerFailedException($"Could not start {Path.GetFileName(fileName)}.");

        using (process)
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            return process.ExitCode;
        }
    }
}
