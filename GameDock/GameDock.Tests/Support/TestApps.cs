using System.Text.RegularExpressions;
using GameDock.Services.Catalog;

namespace GameDock.Tests.Support;

internal static class TestApps
{
    public static ManagedApp Create(string id = "test-app", string exeName = "Test.App.exe") => new()
    {
        Id = id,
        Name = "Test App",
        NameAccent = "Test",
        NameRest = " App",
        Tagline = "Tagline",
        Description = "Description",
        AccentColor = "#112233",
        IconUri = "avares://GameDock/Assets/Apps/test.png",
        Owner = "owner",
        Repository = "repo",
        InnoAppId = "{00000000-0000-0000-0000-000000000000}",
        ExeName = exeName,
        InstallerAssetPattern = new Regex("^Test-.*$")
    };
}
