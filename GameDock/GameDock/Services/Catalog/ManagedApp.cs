using System.Text.RegularExpressions;

namespace GameDock.Services.Catalog;

/// <summary>
/// An application that Game Dock can install, update, remove and open.
/// Every app is published as a GitHub Release with an Inno Setup installer attached.
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

    /// <summary>Pack URI of the icon embedded in Game Dock.</summary>
    public required string IconUri { get; init; }

    public required string Owner { get; init; }

    public required string Repository { get; init; }

    /// <summary>
    /// Inno Setup <c>AppId</c> without the doubled brace, e.g. <c>{42275AB9-...}</c>.
    /// The installer registers the app under <c>Uninstall\{AppId}_is1</c>.
    /// </summary>
    public required string InnoAppId { get; init; }

    /// <summary>Main executable inside the install folder.</summary>
    public required string ExeName { get; init; }

    /// <summary>Matches the Windows installer among the release assets.</summary>
    public required Regex InstallerAssetPattern { get; init; }

    public Uri RepositoryUrl => new($"https://github.com/{Owner}/{Repository}");

    public Uri ReleasesUrl => new($"https://github.com/{Owner}/{Repository}/releases");
}
