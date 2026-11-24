using System.Text.RegularExpressions;

namespace GameDock.Services.Catalog;

/// <summary>
/// Applications managed by App Launcher. AppIds and installer names must match the Inno Setup scripts
/// (<c>installer/*.iss</c>) and release workflows of each repository.
/// </summary>
public static class AppCatalog
{
    private const string Owner = "IgorBabayan";

    private const RegexOptions PatternOptions =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    public static IReadOnlyList<ManagedApp> All { get; } =
    [
        new ManagedApp
        {
            Id = "ed-assistant",
            Name = "ED Assistant",
            NameAccent = "ED",
            NameRest = " Assistant",
            Tagline = "Elite Dangerous companion",
            Description = "Live journal dashboard for exploration, exobiology, materials and ship locker, with plugins.",
            AccentColor = "#FF8A3D",
            IconUri = "pack://application:,,,/GameDock;component/Assets/Apps/ed-assistant.png",
            Owner = Owner,
            Repository = "ed-assistant",
            InnoAppId = "{7C3E5A1B-9F24-4D8E-B6A2-3F1D0C8E5B47}",
            ExeName = "ED.Assistant.exe",
            // ED.Assistant-1.0.42-win-x64-setup.exe
            InstallerAssetPattern = new Regex(@"^ED\.Assistant-.*win-x64-setup\.exe$", PatternOptions)
        },
        new ManagedApp
        {
            Id = "gd-save-editor",
            Name = "GD Save Editor",
            NameAccent = "GD",
            NameRest = " Save Editor",
            Tagline = "Grim Dawn save editor",
            Description = "Edit attributes, equipment, inventory, stashes, materials, waypoints and factions of offline characters.",
            AccentColor = "#E0B252",
            IconUri = "pack://application:,,,/GameDock;component/Assets/Apps/gd-save-editor.png",
            Owner = Owner,
            Repository = "gd-save-editor",
            InnoAppId = "{42275AB9-AC65-4528-A7CB-25AEA463B054}",
            ExeName = "GD.SaveEditor.exe",
            // GD.SaveEditor-Setup-0.1.12.exe
            InstallerAssetPattern = new Regex(@"^GD\.SaveEditor-Setup.*\.exe$", PatternOptions)
        },
        new ManagedApp
        {
            Id = "le-save-editor",
            Name = "LE Save Editor",
            NameAccent = "LE",
            NameRest = " Save Editor",
            Tagline = "Last Epoch save editor",
            Description = "Edit offline characters: equipment, affixes, idols, stash materials, factions, blessings and waypoints.",
            AccentColor = "#A98BFF",
            IconUri = "pack://application:,,,/GameDock;component/Assets/Apps/le-save-editor.png",
            Owner = Owner,
            Repository = "le-save-editor",
            InnoAppId = "{28148D28-7179-40E4-9552-3F6457201A53}",
            ExeName = "LE.SaveEditor.exe",
            // LE.SaveEditor-Setup-0.1.7.exe
            InstallerAssetPattern = new Regex(@"^LE\.SaveEditor-Setup.*\.exe$", PatternOptions)
        },
        new ManagedApp
        {
            Id = "scroll-keepers",
            Name = "Scroll Keeper",
            NameAccent = "Scroll",
            NameRest = " Keeper",
            Tagline = "Elder Scrolls Online addon manager",
            Description = "Browse the ESOUI catalog, install addons with their required libraries, and remove them safely.",
            AccentColor = "#4FC9BE",
            IconUri = "pack://application:,,,/GameDock;component/Assets/Apps/scroll-keepers.png",
            Owner = Owner,
            Repository = "scroll-keepers",
            InnoAppId = "{D7CE39B7-4E58-4D9F-9A5B-C46B7B22708B}",
            ExeName = "ScrollKeeper.exe",
            // ScrollKeeper-Setup-0.1.12.exe
            InstallerAssetPattern = new Regex(@"^ScrollKeeper-Setup.*\.exe$", PatternOptions)
        },
        new ManagedApp
        {
            Id = "navigator",
            Name = "Navigator",
            NameAccent = "Nav",
            NameRest = "igator",
            Tagline = "File and folder navigator",
            Description = "Navigate the files and folders of your OS quickly from a single, keyboard-friendly window.",
            AccentColor = "#5AA9FF",
            IconUri = "pack://application:,,,/GameDock;component/Assets/Apps/navigator.png",
            Owner = Owner,
            Repository = "navigator",
            InnoAppId = "{5B0E3C59-7E3A-4A92-9D7C-2F6A1C4B8E11}",
            ExeName = "Navigator.exe",
            // Navigator-1.0.3-win-x64-setup.exe
            InstallerAssetPattern = new Regex(@"^Navigator-.*win-x64-setup\.exe$", PatternOptions)
        }
    ];
}
