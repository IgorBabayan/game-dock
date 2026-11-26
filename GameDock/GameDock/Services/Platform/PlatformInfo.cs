using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace GameDock.Services.Platform;

/// <summary>The operating system App Launcher runs on, and what that means for installers and data folders.</summary>
public static class PlatformInfo
{
    /// <summary>.NET-style runtime identifier of this machine: <c>win-x64</c>, <c>linux-arm64</c>, <c>osx-arm64</c>...</summary>
    public static string RuntimeId { get; } = $"{OsId}-{ArchitectureId(RuntimeInformation.OSArchitecture)}";

    /// <summary>"Windows", "macOS" or "Linux".</summary>
    public static string OsName { get; } =
        OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : "Linux";

    /// <summary>What a release must contain to be installable here, e.g. "Windows installer" or "Linux build".</summary>
    public static string InstallerKind { get; } = OperatingSystem.IsWindows() ? "Windows installer" : $"{OsName} build";

    /// <summary>Card status when the latest release has nothing for this platform.</summary>
    public static string MissingInstallerStatus { get; } = $"NO {InstallerKind.ToUpperInvariant()}";

    /// <summary>
    /// Per-user settings folder: <c>%APPDATA%\GameDock</c> on Windows (unchanged from earlier versions),
    /// <c>~/.config/GameDock</c> on Linux and <c>~/Library/Application Support/GameDock</c> on macOS.
    /// </summary>
    public static string SettingsFolder { get; } = Path.Combine(
        GetFolder(OperatingSystem.IsMacOS()
            ? Environment.SpecialFolder.LocalApplicationData
            : Environment.SpecialFolder.ApplicationData),
        "GameDock");

    /// <summary>
    /// Where portable (archive) builds of the managed apps are unpacked on Linux and macOS:
    /// <c>~/.local/share/GameDock/Apps</c> or <c>~/Library/Application Support/GameDock/Apps</c>.
    /// </summary>
    public static string PortableAppsFolder { get; } = Path.Combine(
        GetFolder(Environment.SpecialFolder.LocalApplicationData),
        "GameDock",
        "Apps");

    /// <summary>
    /// Matches portable archives for this machine, named <c>&lt;prefix&gt;&lt;version&gt;-&lt;rid&gt;.tar.gz</c>
    /// (or <c>.tgz</c> / <c>.zip</c>), e.g. <c>ED.Assistant-1.0.42-linux-x64.tar.gz</c>.
    /// </summary>
    /// <remarks>Built once per app at startup; interpreted (not compiled) because it runs a handful of times.</remarks>
    public static Regex PortableArchivePattern(string fileNamePrefix) => new(
        $@"^{Regex.Escape(fileNamePrefix)}.*-{Regex.Escape(RuntimeId)}\.(tar\.gz|tgz|zip)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// DoNotVerify: on a fresh Linux account <c>~/.config</c> or <c>~/.local/share</c> may not exist yet, and the
    /// default lookup would then return an empty string (= the current directory). The folders are created on write.
    /// </summary>
    private static string GetFolder(Environment.SpecialFolder folder) =>
        Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);

    private static string OsId =>
        OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";

    private static string ArchitectureId(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        Architecture.Arm => "arm",
        _ => architecture.ToString().ToLowerInvariant()
    };
}
