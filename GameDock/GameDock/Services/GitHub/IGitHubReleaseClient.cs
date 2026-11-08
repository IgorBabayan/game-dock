using System.Text.RegularExpressions;

namespace GameDock.Services.GitHub;

/// <summary>Reads GitHub Releases and downloads their assets.</summary>
public interface IGitHubReleaseClient
{
    /// <summary>
    /// Returns the release marked "Latest" (drafts and pre-releases are ignored), or <c>null</c> when the
    /// repository has no published release yet.
    /// </summary>
    /// <param name="installerPattern">Selects the Windows installer among the release assets.</param>
    /// <exception cref="GitHubRateLimitException">The anonymous rate limit is used up.</exception>
    Task<ReleaseInfo?> GetLatestReleaseAsync(
        string owner,
        string repository,
        Regex installerPattern,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads <paramref name="asset"/> into <paramref name="folder"/>, verifies its size and SHA-256
    /// and returns the local file path.
    /// </summary>
    Task<string> DownloadAsync(
        ReleaseAsset asset,
        string folder,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken = default);
}
