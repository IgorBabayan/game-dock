using System.IO;

namespace GameDock.Services.Installation;

/// <summary>Where and which version of an app is installed, as registered by its Inno Setup installer.</summary>
/// <param name="Version">Installed version (<c>DisplayVersion</c>), or <c>null</c> if it could not be read.</param>
/// <param name="InstallFolder">Install folder (<c>InstallLocation</c>).</param>
/// <param name="UninstallerPath">Full path of <c>unins000.exe</c>.</param>
/// <param name="IsAllUsers">Installed for all users (HKLM, needs admin rights to change) rather than per user (HKCU).</param>
public sealed record InstalledApp(
    Version? Version,
    string InstallFolder,
    string? UninstallerPath,
    bool IsAllUsers)
{
    public string GetExePath(string exeName) => Path.Combine(InstallFolder, exeName);
}
