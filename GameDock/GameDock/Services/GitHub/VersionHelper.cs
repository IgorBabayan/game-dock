namespace GameDock.Services.GitHub;

/// <summary>Version parsing shared by release tags and installed versions.</summary>
public static class VersionHelper
{
    /// <summary>Parses <c>v0.1.12</c>, <c>0.1.12</c>, <c>1.0.42-beta</c> and similar.</summary>
    public static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag))
            return false;

        var text = tag.Trim().TrimStart('v', 'V');
        // Ignore any suffix such as "-beta" or "+build".
        var end = text.IndexOfAny(['-', '+', ' ']);
        if (end >= 0)
            text = text[..end];

        if (!Version.TryParse(text, out var parsed))
            return false;

        version = Normalize(parsed);
        return true;
    }

    /// <summary>Compares only Major.Minor.Build so 0.1.12 and 0.1.12.0 are equal.</summary>
    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    /// <summary>"0.1.12" (always three parts).</summary>
    public static string Format(Version v) => Normalize(v).ToString(3);
}
