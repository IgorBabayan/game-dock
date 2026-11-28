using GameDock.Services.Installation;

namespace GameDock.Tests.Services;

public class InstalledAppTests
{
    [Fact]
    public void GetExePath_InnoSetupInstall_CombinesInstallFolderAndExeName()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Apps", "Navigator");
        var app = new InstalledApp(new Version(1, 0, 3), folder, Path.Combine(folder, "unins000.exe"), IsAllUsers: false);

        Assert.Equal(Path.Combine(folder, "Navigator.exe"), app.GetExePath("Navigator.exe"));
    }

    [Fact]
    public void GetExePath_PortableInstall_UsesEntryPoint()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Apps", "navigator");
        var entry = Path.Combine(folder, "Navigator");
        var app = new InstalledApp(null, folder, null, IsAllUsers: false, EntryPoint: entry);

        Assert.Equal(entry, app.GetExePath("Navigator.exe"));
    }
}
