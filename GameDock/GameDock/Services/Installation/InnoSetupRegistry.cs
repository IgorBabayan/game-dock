using System.Diagnostics;
using System.IO;
using System.Security;
using GameDock.Services.Catalog;
using GameDock.Services.GitHub;
using Microsoft.Win32;

namespace GameDock.Services.Installation;

/// <summary>
/// Reads <c>Software\Microsoft\Windows\CurrentVersion\Uninstall\{AppId}_is1</c>, which Inno Setup writes
/// to HKCU for per-user installs and to HKLM for all-users installs.
/// </summary>
public sealed class InnoSetupRegistry : IInstalledAppRegistry
{
    private const string UninstallRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    public InstalledApp? Find(ManagedApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var keyName = $@"{UninstallRoot}\{app.InnoAppId}_is1";

        // Per-user first: that is the installers' default (PrivilegesRequired=lowest).
        return Read(RegistryHive.CurrentUser, RegistryView.Default, keyName, app, isAllUsers: false)
               ?? Read(RegistryHive.LocalMachine, RegistryView.Registry64, keyName, app, isAllUsers: true)
               ?? Read(RegistryHive.LocalMachine, RegistryView.Registry32, keyName, app, isAllUsers: true);
    }

    private static InstalledApp? Read(
        RegistryHive hive,
        RegistryView view,
        string keyName,
        ManagedApp app,
        bool isAllUsers)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey(keyName);
            if (key is null)
                return null;

            var folder = (key.GetValue("InstallLocation") as string)
                         ?? (key.GetValue("Inno Setup: App Path") as string);
            if (string.IsNullOrWhiteSpace(folder))
                return null;

            folder = folder.Trim().Trim('"');

            // The key can outlive the files (e.g. the folder was deleted by hand): treat as not installed.
            if (!File.Exists(Path.Combine(folder, app.ExeName)))
                return null;

            Version? version = VersionHelper.TryParseTag(key.GetValue("DisplayVersion") as string, out var parsed)
                ? parsed
                : null;

            return new InstalledApp(version, folder, ParseExePath(key.GetValue("UninstallString") as string), isAllUsers);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            Debug.WriteLine($"Could not read {hive}\\{keyName}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Inno Setup stores <c>"C:\...\unins000.exe"</c> (quoted, without arguments).</summary>
    private static string? ParseExePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }

        var exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? command[..(exe + 4)] : command;
    }
}
