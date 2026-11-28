using GameDock.Services.Platform;

namespace GameDock.Tests.Services;

public class PlatformInfoTests
{
    [Fact]
    public void RuntimeId_MatchesCurrentOperatingSystem()
    {
        var prefix = OperatingSystem.IsWindows() ? "win-" : OperatingSystem.IsMacOS() ? "osx-" : "linux-";
        Assert.StartsWith(prefix, PlatformInfo.RuntimeId);
        Assert.Equal(PlatformInfo.RuntimeId.ToLowerInvariant(), PlatformInfo.RuntimeId);
    }

    [Fact]
    public void OsName_And_InstallerKind_AreConsistent()
    {
        Assert.Contains(PlatformInfo.OsName, new[] { "Windows", "macOS", "Linux" });
        Assert.Equal(
            OperatingSystem.IsWindows() ? "Windows installer" : $"{PlatformInfo.OsName} build",
            PlatformInfo.InstallerKind);
        Assert.Equal($"NO {PlatformInfo.InstallerKind.ToUpperInvariant()}", PlatformInfo.MissingInstallerStatus);
    }

    [Fact]
    public void Folders_AreAbsoluteAndNamedAfterTheApp()
    {
        Assert.True(Path.IsPathRooted(PlatformInfo.SettingsFolder));
        Assert.Equal("GameDock", Path.GetFileName(PlatformInfo.SettingsFolder));

        Assert.True(Path.IsPathRooted(PlatformInfo.PortableAppsFolder));
        Assert.Equal("Apps", Path.GetFileName(PlatformInfo.PortableAppsFolder));
        Assert.Equal("GameDock", Path.GetFileName(Path.GetDirectoryName(PlatformInfo.PortableAppsFolder)));
    }

    [Theory]
    [InlineData("tar.gz")]
    [InlineData("tgz")]
    [InlineData("zip")]
    [InlineData("TAR.GZ")]
    public void PortableArchivePattern_MatchesArchivesForThisMachine(string extension)
    {
        var pattern = PlatformInfo.PortableArchivePattern("ED.Assistant-");

        Assert.Matches(pattern, $"ED.Assistant-1.0.42-{PlatformInfo.RuntimeId}.{extension}");
    }

    [Theory]
    [InlineData("ED.Assistant-1.0.42-{0}.exe")]          // wrong extension
    [InlineData("ED.Assistant-1.0.42-{0}.tar.gz.sha256")] // checksum sidecar
    [InlineData("EDxAssistant-1.0.42-{0}.tar.gz")]        // "." in the prefix is literal
    [InlineData("Other-1.0.42-{0}.tar.gz")]               // other app
    [InlineData("ED.Assistant-1.0.42-other-rid.tar.gz")]  // other machine
    public void PortableArchivePattern_RejectsOtherFiles(string format)
    {
        var pattern = PlatformInfo.PortableArchivePattern("ED.Assistant-");

        Assert.DoesNotMatch(pattern, string.Format(format, PlatformInfo.RuntimeId));
    }
}
