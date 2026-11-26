using System.ComponentModel;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using GameDock.Services.Catalog;
using GameDock.Services.GitHub;
using GameDock.Services.Platform;

namespace GameDock.Services.Installation;

/// <summary>
/// Linux / macOS: installs the apps from portable release archives (<c>.tar.gz</c> / <c>.zip</c>) into
/// <see cref="PlatformInfo.PortableAppsFolder"/>, per user and without admin rights.
/// Updates unpack next to the current version and swap folders, so a failed update keeps the old version.
/// Only one operation runs at a time; the others wait in line.
/// </summary>
public sealed class PortableAppInstaller(
    IGitHubReleaseClient releaseClient,
    IInstalledAppRegistry registry) : IAppInstaller, IDisposable
{
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

        var archiveAsset = release.Installer
                           ?? throw new InstallerFailedException(
                               $"Release {release.TagName} of {app.Name} has no {PlatformInfo.InstallerKind}.");

        await EnterAsync(onStage, cancellationToken).ConfigureAwait(false);
        try
        {
            onStage?.Invoke(InstallStage.Downloading);
            var archive = await releaseClient
                .DownloadAsync(archiveAsset, Path.Combine(DownloadRoot, app.Id), progress, cancellationToken)
                .ConfigureAwait(false);

            // Past this point the installation runs to completion: cancelling halfway would leave a broken install.
            onStage?.Invoke(InstallStage.Installing);

            // Unpacking is disk-bound: keep it off the UI thread.
            await Task.Run(() => Unpack(app, release, archive), CancellationToken.None).ConfigureAwait(false);
            TryDeleteFile(archive);

            if (registry.Find(app) is null)
                throw new InstallerFailedException($"{app.Name} was not found after installation.");
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

        var folder = Path.GetFullPath(installed.InstallFolder);

        // Never delete anything App Launcher did not install itself.
        if (!IsInsideAppsFolder(folder))
            throw new InstallerFailedException($"{app.Name} was not installed by App Launcher and cannot be removed here.");

        await EnterAsync(null, cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() => Directory.Delete(folder, recursive: true), CancellationToken.None).ConfigureAwait(false);

            if (registry.Find(app) is not null)
                throw new InstallerFailedException($"{app.Name} could not be removed completely.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Launch(ManagedApp app, InstalledApp installed)
    {
        var entry = installed.GetExePath(app.ExeName);

        // macOS application bundle: let LaunchServices start it (Dock icon, single instance, permissions).
        if (OperatingSystem.IsMacOS() && entry.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && Directory.Exists(entry))
        {
            var open = new ProcessStartInfo("open") { UseShellExecute = false };
            open.ArgumentList.Add(entry);
            Process.Start(open)?.Dispose();
            return;
        }

        if (!File.Exists(entry))
            throw new FileNotFoundException($"{app.Name} executable was not found.", entry);

        Process.Start(new ProcessStartInfo
        {
            FileName = entry,
            WorkingDirectory = Path.GetDirectoryName(entry) ?? installed.InstallFolder,
            UseShellExecute = false
        })?.Dispose();
    }

    public bool IsRunning(ManagedApp app, InstalledApp installed)
    {
        var folder = Path.GetFullPath(installed.InstallFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var processes = Process.GetProcessesByName(ExecutableName(app));
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (path is null || path.StartsWith(folder, StringComparison.Ordinal))
                        return true;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Another user's process or already exited: the name matches, assume it is ours.
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
            Debug.WriteLine($"Download cleanup failed: {ex.Message}");
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

    /// <summary>Unpacks <paramref name="archive"/> next to the install folder, then swaps the folders.</summary>
    private static void Unpack(ManagedApp app, ReleaseInfo release, string archive)
    {
        var target = PortableAppRegistry.GetInstallFolder(app);
        var staging = target + ".new";
        var backup = target + ".old";

        DeleteDirectory(staging);
        Directory.CreateDirectory(staging);

        try
        {
            Extract(archive, staging);

            var entryPoint = FindEntryPoint(app, staging)
                             ?? throw new InstallerFailedException(
                                 $"The {app.Name} archive does not contain {ExecutableName(app)}.");

            if (!Directory.Exists(entryPoint))
                EnsureExecutable(entryPoint);

            var manifest = new PortableManifest(
                release.TagName,
                Path.GetRelativePath(staging, entryPoint),
                DateTimeOffset.Now);
            File.WriteAllText(
                Path.Combine(staging, PortableAppRegistry.ManifestFileName),
                JsonSerializer.Serialize(manifest, PortableJsonContext.Default.PortableManifest));

            // Swap: current -> .old, new -> current. Running apps keep working (their files stay open).
            DeleteDirectory(backup);
            if (Directory.Exists(target))
                Directory.Move(target, backup);

            try
            {
                Directory.Move(staging, target);
            }
            catch
            {
                if (Directory.Exists(backup) && !Directory.Exists(target))
                    Directory.Move(backup, target);
                throw;
            }

            TryDeleteDirectory(backup);
        }
        catch
        {
            TryDeleteDirectory(staging);
            throw;
        }
    }

    /// <summary>Extracts a <c>.zip</c> or <c>.tar.gz</c>; both reject entries that would land outside the folder.</summary>
    private static void Extract(string archive, string destination)
    {
        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            // Restores Unix permissions stored in the archive.
            ZipFile.ExtractToDirectory(archive, destination, overwriteFiles: true);
            return;
        }

        using var file = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read, 81920);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        // Keeps file modes and symbolic links (e.g. inside macOS bundles).
        TarFile.ExtractToDirectory(gzip, destination, overwriteFiles: true);
    }

    /// <summary>
    /// The app to start: on macOS an <c>.app</c> bundle if the archive has one, otherwise the executable named like
    /// <see cref="ManagedApp.ExeName"/> without <c>.exe</c> (top level or a few folders down).
    /// </summary>
    private static string? FindEntryPoint(ManagedApp app, string root)
    {
        if (OperatingSystem.IsMacOS())
        {
            var bundle = Directory
                .EnumerateDirectories(root, "*.app", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    MaxRecursionDepth = 1,
                    MatchCasing = MatchCasing.CaseInsensitive
                })
                .FirstOrDefault();
            if (bundle is not null)
                return bundle;
        }

        return Directory
            .EnumerateFiles(root, ExecutableName(app), new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = 3,
                MatchCasing = MatchCasing.CaseInsensitive
            })
            .FirstOrDefault();
    }

    private static string ExecutableName(ManagedApp app) => Path.GetFileNameWithoutExtension(app.ExeName);

    private static void EnsureExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        var mode = File.GetUnixFileMode(path);
        const UnixFileMode execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        if ((mode & UnixFileMode.UserExecute) == 0)
            File.SetUnixFileMode(path, mode | execute);
    }

    private static bool IsInsideAppsFolder(string folder)
    {
        var root = Path.GetFullPath(PlatformInfo.PortableAppsFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return folder.StartsWith(root, StringComparison.Ordinal) && folder.Length > root.Length;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            DeleteDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not delete {path}: {ex.Message}");
        }
    }

    private static void TryDeleteFile(string path)
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
}
