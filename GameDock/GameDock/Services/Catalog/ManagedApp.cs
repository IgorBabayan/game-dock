using System.Text.RegularExpressions;

namespace GameDock.Services.Catalog;

/// <summary>
/// An application that App Launcher can install, update, remove and open.
/// Every app is published as a GitHub Release with an Inno Setup installer (Windows) and, optionally,
/// portable archives for Linux and macOS attached.
/// </summary>
public sealed record ManagedApp
{
    /// <summary>Stable identifier (the GitHub repository name).</summary>
    public required string Id { get; init; }

    /// <summary>Full display name, e.g. "GD Save Editor".</summary>
    public required string Name { get; init; }

    /// <summary>Highlighted part of the name, drawn in <see cref="AccentColor"/> (e.g. "GD").</summary>
    public required string NameAccent { get; init; }

    /// <summary>Rest of the name drawn after <see cref="NameAccent"/> (e.g. " Save Editor").</summary>
    public required string NameRest { get; init; }

    /// <summary>One-line subtitle under the name.</summary>
    public required string Tagline { get; init; }

    /// <summary>Short description shown on the card.</summary>
    public required string Description { get; init; }

    /// <summary>Accent color (#RRGGBB) used for the name and the installed version.</summary>
    public required string AccentColor { get; init; }

    /// <summary><c>avares://</c> URI of the icon embedded in App Launcher.</summary>
    public required string IconUri { get; init; }

    public required string Owner { get; init; }

    public required string Repository { get; init; }

    /// <summary>
    /// Inno Setup <c>AppId</c> without the doubled brace, e.g. <c>{42275AB9-...}</c>.
    /// The installer registers the app under <c>Uninstall\{AppId}_is1</c>.
    /// </summary>
    public required string InnoAppId { get; init; }

    /// <summary>
    /// Main executable inside the install folder (Windows). On Linux / macOS the same name without <c>.exe</c>
    /// (or an <c>.app</c> bundle on macOS) is looked up inside the unpacked archive.
    /// </summary>
    public required string ExeName { get; init; }

    /// <summary>Matches the installer for this platform among the release assets (see <see cref="AppCatalog"/>).</summary>
    public required Regex InstallerAssetPattern { get; init; }

    public Uri RepositoryUrl => new($"https://github.com/{Owner}/{Repository}");

    public Uri ReleasesUrl => new($"https://github.com/{Owner}/{Repository}/releases");
}
