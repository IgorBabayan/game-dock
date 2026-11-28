using GameDock.Services.Settings;

namespace GameDock.Tests.Services;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_AreAsDocumented()
    {
        var settings = new AppSettings();

        Assert.True(settings.AutoUpdate);
        Assert.Equal(5, settings.RestartDelaySeconds);
        Assert.Equal(AppViewMode.Grid, settings.ViewMode);
        Assert.Equal(AppSortMode.NameAscending, settings.SortMode);
        Assert.InRange(settings.RestartDelaySeconds, 0, AppSettings.MaxRestartDelaySeconds);
    }

    [Fact]
    public void With_CreatesAChangedCopy()
    {
        var original = new AppSettings();
        var changed = original with { AutoUpdate = false, ViewMode = AppViewMode.List };

        Assert.True(original.AutoUpdate);
        Assert.False(changed.AutoUpdate);
        Assert.Equal(AppViewMode.List, changed.ViewMode);
        Assert.NotEqual(original, changed);
        Assert.Equal(original, new AppSettings());
    }
}
