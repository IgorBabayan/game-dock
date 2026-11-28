using GameDock.Services.GitHub;

namespace GameDock.Tests.Services;

public class VersionHelperTests
{
    [Theory]
    [InlineData("v0.1.12", 0, 1, 12)]
    [InlineData("V0.1.12", 0, 1, 12)]
    [InlineData("0.1.12", 0, 1, 12)]
    [InlineData("  v1.2.3  ", 1, 2, 3)]
    [InlineData("1.0.42-beta", 1, 0, 42)]
    [InlineData("1.0.42+build.7", 1, 0, 42)]
    [InlineData("2.5.1 final", 2, 5, 1)]
    [InlineData("1.2", 1, 2, 0)]
    [InlineData("1.2.3.4", 1, 2, 3)]
    public void TryParseTag_ParsesKnownFormats(string tag, int major, int minor, int build)
    {
        Assert.True(VersionHelper.TryParseTag(tag, out var version));
        Assert.Equal(new Version(major, minor, build), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("latest")]
    [InlineData("v")]
    [InlineData("1")]
    [InlineData("-beta")]
    public void TryParseTag_RejectsInvalidTags(string? tag)
    {
        Assert.False(VersionHelper.TryParseTag(tag, out var version));
        Assert.Equal(new Version(0, 0, 0), version);
    }

    [Fact]
    public void Normalize_TreatsMissingBuildAsZero()
    {
        Assert.Equal(new Version(1, 2, 0), VersionHelper.Normalize(new Version(1, 2)));
    }

    [Fact]
    public void Normalize_DropsRevision()
    {
        Assert.Equal(VersionHelper.Normalize(new Version(0, 1, 12)), VersionHelper.Normalize(new Version(0, 1, 12, 0)));
        Assert.Equal(new Version(0, 1, 12), VersionHelper.Normalize(new Version(0, 1, 12, 99)));
    }

    [Theory]
    [InlineData(1, 2, -1, "1.2.0")]
    [InlineData(0, 1, 12, "0.1.12")]
    public void Format_AlwaysHasThreeParts(int major, int minor, int build, string expected)
    {
        var version = build < 0 ? new Version(major, minor) : new Version(major, minor, build);
        Assert.Equal(expected, VersionHelper.Format(version));
    }

    [Fact]
    public void Format_IgnoresRevision()
    {
        Assert.Equal("3.4.5", VersionHelper.Format(new Version(3, 4, 5, 6)));
    }
}
