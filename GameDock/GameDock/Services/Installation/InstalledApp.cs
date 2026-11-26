using System.IO;

namespace GameDock.Services.Installation;

/// <summary>
/// Where and which version of an app is installed, as registered by its Inno Setup installer (Windows)
/// or by App Launcher's portable installer (Linux / macOS).
/// </summary>
/// <param name="Version">Installed version (<c>DisplayVersion</c>), or <c>null</c> if it could not be read.</param>
/// <param name="InstallFolder">Install folder (<c>InstallLocation</c>).</param>
/// <param name="UninstallerPath">Full path of <c>unins000.exe</c>.</param>
/// <param name="IsAllUsers">Installed for all users (HKLM, needs admin rights to change) rather than per user (HKCU).</param>
/// <param name="EntryPoint">
/// Portable installs: full path of the executable or macOS <c>.app</c> bundle to start; <c>null</c> for Inno Setup installs.
/// </param>
public sealed record InstalledApp(
    Version? Version,
    string InstallFolder,
    string? UninstallerPath,
    bool IsAllUsers,
    string? EntryPoint = null)
{
    public string GetExePath(string exeName) => EntryPoint ?? Path.Combine(InstallFolder, exeName);
}
