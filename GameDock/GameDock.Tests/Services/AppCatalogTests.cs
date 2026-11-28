using GameDock.Services.Catalog;
using GameDock.Services.Platform;

namespace GameDock.Tests.Services;

public class AppCatalogTests
{
    [Fact]
    public void All_IsNotEmpty()
    {
        Assert.NotEmpty(AppCatalog.All);
    }

    [Fact]
    public void All_HasUniqueIdsRepositoriesAndInnoAppIds()
    {
        Assert.Equal(AppCatalog.All.Count, AppCatalog.All.Select(a => a.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(AppCatalog.All.Count, AppCatalog.All.Select(a => a.Repository).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(AppCatalog.All.Count, AppCatalog.All.Select(a => a.InnoAppId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void All_HasValidMetadata()
    {
        foreach (var app in AppCatalog.All)
        {
            Assert.Equal(app.Name, app.NameAccent + app.NameRest);
            Assert.Matches("^#[0-9A-Fa-f]{6}$", app.AccentColor);
            Assert.StartsWith("avares://GameDock/Assets/Apps/", app.IconUri);
            Assert.EndsWith(".exe", app.ExeName);
            Assert.False(string.IsNullOrWhiteSpace(app.Tagline));
            Assert.False(string.IsNullOrWhiteSpace(app.Description));

            // Inno Setup AppId: a braced GUID ({{...} in the .iss file).
            Assert.StartsWith("{", app.InnoAppId);
            Assert.EndsWith("}", app.InnoAppId);
            Assert.True(Guid.TryParse(app.InnoAppId, out _), $"{app.Id}: invalid InnoAppId {app.InnoAppId}");
        }
    }

    [Fact]
    public void Icons_AreEmbeddedInTheAppAssets()
    {
        // The test output does not contain the source tree; find the repository from the test assembly folder.
        var assets = FindAssetsFolder();
        if (assets is null)
            return;

        foreach (var app in AppCatalog.All)
        {
            var file = Path.Combine(assets, Path.GetFileName(app.IconUri));
            Assert.True(File.Exists(file), $"{app.Id}: missing icon {file}");
        }
    }

    [Fact]
    public void Urls_PointToTheGitHubRepository()
    {
        foreach (var app in AppCatalog.All)
        {
            Assert.Equal($"https://github.com/{app.Owner}/{app.Repository}", app.RepositoryUrl.AbsoluteUri);
            Assert.Equal($"https://github.com/{app.Owner}/{app.Repository}/releases", app.ReleasesUrl.AbsoluteUri);
        }
    }

    /// <summary>Real asset names published by each app's release workflow, for this platform.</summary>
    private static IEnumerable<(string Id, string Asset)> PublishedAssets()
    {
        var rid = PlatformInfo.RuntimeId;
        var windows = OperatingSystem.IsWindows();
        yield return ("ed-assistant", windows ? "ED.Assistant-1.0.42-win-x64-setup.exe" : $"ED.Assistant-1.0.42-{rid}.tar.gz");
        yield return ("gd-save-editor", windows ? "GD.SaveEditor-Setup-0.1.12.exe" : $"GD.SaveEditor-0.1.12-{rid}.tar.gz");
        yield return ("le-save-editor", windows ? "LE.SaveEditor-Setup-0.1.7.exe" : $"LE.SaveEditor-0.1.7-{rid}.zip");
        yield return ("scroll-keepers", windows ? "ScrollKeeper-Setup-0.1.12.exe" : $"ScrollKeeper-0.1.12-{rid}.tgz");
        yield return ("navigator", windows ? "Navigator-1.0.3-win-x64-setup.exe" : $"Navigator-1.0.3-{rid}.tar.gz");
        yield return ("exile-lens", windows ? "ExileLens-1.2.3-win-x64-setup.exe" : $"ExileLens-1.2.3-{rid}.zip");
    }

    public static TheoryData<string, string> InstallerNames()
    {
        var data = new TheoryData<string, string>();
        foreach (var (id, asset) in PublishedAssets())
            data.Add(id, asset);
        return data;
    }

    [Fact]
    public void PublishedAssets_CoverTheWholeCatalog()
    {
        Assert.Equal(
            AppCatalog.All.Select(a => a.Id).Order(),
            PublishedAssets().Select(a => a.Id).Order());
    }

    [Theory]
    [MemberData(nameof(InstallerNames))]
    public void InstallerAssetPattern_MatchesThePublishedAssetForThisPlatform(string id, string assetName)
    {
        var app = Assert.Single(AppCatalog.All, a => a.Id == id);

        Assert.Matches(app.InstallerAssetPattern, assetName);
    }

    [Fact]
    public void InstallerAssetPattern_DoesNotMatchOtherAppsAssets()
    {
        foreach (var app in AppCatalog.All)
        foreach (var (id, asset) in PublishedAssets().Where(a => a.Id != app.Id))
            Assert.False(app.InstallerAssetPattern.IsMatch(asset), $"{app.Id} matches the asset of {id}: {asset}");
    }

    [Fact]
    public void InstallerAssetPattern_IgnoresSourceArchives()
    {
        foreach (var app in AppCatalog.All)
        {
            Assert.DoesNotMatch(app.InstallerAssetPattern, "Source code (zip)");
            Assert.DoesNotMatch(app.InstallerAssetPattern, "Source code (tar.gz)");
        }
    }

    private static string? FindAssetsFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "GameDock", "Assets", "Apps");
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
