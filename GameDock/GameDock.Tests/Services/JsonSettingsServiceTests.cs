using GameDock.Services.Settings;
using GameDock.Tests.Support;

namespace GameDock.Tests.Services;

public class JsonSettingsServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    private string FilePath => _temp.Combine("nested", "settings.json");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var service = new JsonSettingsService(FilePath);

        Assert.Equal(new AppSettings(), service.Current);
    }

    [Fact]
    public void Save_PersistsAndReloads()
    {
        var settings = new AppSettings
        {
            AutoUpdate = false,
            RestartDelaySeconds = 42,
            ViewMode = AppViewMode.List,
            SortMode = AppSortMode.RecentlyUpdated
        };

        new JsonSettingsService(FilePath).Save(settings);

        Assert.Equal(settings, new JsonSettingsService(FilePath).Current);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void Save_UpdatesCurrentAndRaisesSettingsChanged()
    {
        var service = new JsonSettingsService(FilePath);
        var changed = new AppSettings { SortMode = AppSortMode.NameDescending };
        AppSettings? raised = null;
        service.SettingsChanged += (_, s) => raised = s;

        service.Save(changed);

        Assert.Same(changed, service.Current);
        Assert.Same(changed, raised);
    }

    [Fact]
    public void Save_Null_Throws()
    {
        var service = new JsonSettingsService(FilePath);

        Assert.Throws<ArgumentNullException>(() => service.Save(null!));
    }

    [Fact]
    public void Save_WritesEnumsAsNames()
    {
        new JsonSettingsService(FilePath).Save(new AppSettings { ViewMode = AppViewMode.List });

        var json = File.ReadAllText(FilePath);
        Assert.Contains("\"List\"", json);
        Assert.Contains("\"NameAscending\"", json);
    }

    [Fact]
    public void Load_CorruptedFile_ReturnsDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ not json");

        Assert.Equal(new AppSettings(), new JsonSettingsService(FilePath).Current);
    }

    [Fact]
    public void Load_OlderFileWithMissingProperties_KeepsDefaultsForThem()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, """{ "AutoUpdate": false }""");

        var current = new JsonSettingsService(FilePath).Current;

        Assert.False(current.AutoUpdate);
        Assert.Equal(5, current.RestartDelaySeconds);
        Assert.Equal(AppViewMode.Grid, current.ViewMode);
    }

    [Fact]
    public void Load_NullDocument_ReturnsDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "null");

        Assert.Equal(new AppSettings(), new JsonSettingsService(FilePath).Current);
    }
}
