using GameDock.Services.GitHub;

namespace GameDock.Tests.Services;

public class ReleaseInfoTests
{
    [Theory]
    [InlineData(0, 100, 0.0)]
    [InlineData(50, 100, 0.5)]
    [InlineData(100, 100, 1.0)]
    [InlineData(150, 100, 1.0)]
    public void DownloadProgress_Fraction_IsClampedToZeroOne(long received, long total, double expected)
    {
        Assert.Equal(expected, new DownloadProgress(received, total).Fraction);
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(10, -1)]
    public void DownloadProgress_Fraction_IsNullWhenTotalUnknown(long received, long total)
    {
        Assert.Null(new DownloadProgress(received, total).Fraction);
    }

    [Fact]
    public void RateLimitException_WithoutReset_SaysTryLater()
    {
        var ex = new GitHubRateLimitException(null);

        Assert.Null(ex.ResetsAt);
        Assert.Contains("Try again later", ex.Message);
    }

    [Fact]
    public void RateLimitException_WithReset_KeepsResetTime()
    {
        var resetsAt = DateTimeOffset.UtcNow.AddMinutes(30);
        var ex = new GitHubRateLimitException(resetsAt);

        Assert.Equal(resetsAt, ex.ResetsAt);
        Assert.Contains("Try again after", ex.Message);
        Assert.IsAssignableFrom<HttpRequestException>(ex);
    }
}
