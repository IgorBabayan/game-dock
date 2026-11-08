using System.Net.Http;

namespace GameDock.Services.GitHub;

/// <summary>Latest published GitHub Release of a repository.</summary>
/// <param name="Version">Version parsed from the tag (e.g. <c>v0.1.12</c> → 0.1.12).</param>
/// <param name="TagName">Raw tag name.</param>
/// <param name="PublishedAt">When the release was published.</param>
/// <param name="PageUrl">Web page of the release.</param>
/// <param name="Installer">Windows installer asset, or <c>null</c> if the release has none.</param>
public sealed record ReleaseInfo(
    Version Version,
    string TagName,
    DateTimeOffset? PublishedAt,
    Uri? PageUrl,
    ReleaseAsset? Installer);

/// <summary>A downloadable file attached to a release.</summary>
/// <param name="Name">File name.</param>
/// <param name="DownloadUrl">Direct download URL.</param>
/// <param name="Size">Size in bytes, or 0 if unknown.</param>
/// <param name="Sha256">Expected SHA-256 (lower-case hex) published by GitHub, if available.</param>
public sealed record ReleaseAsset(string Name, Uri DownloadUrl, long Size, string? Sha256);

/// <summary>Download progress report.</summary>
/// <param name="BytesReceived">Bytes downloaded so far.</param>
/// <param name="TotalBytes">Total size in bytes, or 0 if unknown.</param>
public readonly record struct DownloadProgress(long BytesReceived, long TotalBytes)
{
    /// <summary>Completed fraction in the range 0..1, or <c>null</c> if the total size is unknown.</summary>
    public double? Fraction => TotalBytes > 0 ? Math.Clamp((double)BytesReceived / TotalBytes, 0, 1) : null;
}

/// <summary>GitHub refused the request because the anonymous API rate limit (60 requests per hour) is used up.</summary>
public sealed class GitHubRateLimitException(DateTimeOffset? resetsAt)
    : HttpRequestException(resetsAt is { } at
        ? $"GitHub rate limit reached. Try again after {at.ToLocalTime():t}."
        : "GitHub rate limit reached. Try again later.")
{
    public DateTimeOffset? ResetsAt { get; } = resetsAt;
}
