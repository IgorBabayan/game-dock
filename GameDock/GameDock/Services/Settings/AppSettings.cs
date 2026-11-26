namespace GameDock.Services.Settings;

/// <summary>User preferences persisted between application runs.</summary>
/// <remarks>
/// Properties use <c>set</c> rather than <c>init</c> on purpose: the source-generated JSON reader then assigns only
/// the properties present in the file, so a value missing from an older settings file keeps its default below
/// (with <c>init</c> it would be reset to 0 / false). Treat instances as immutable and change them with <c>with</c>.
/// </remarks>
public sealed record AppSettings
{
    /// <summary>
    /// When enabled, the latest GitHub release of App Launcher is checked on every start and, if newer,
    /// downloaded, installed and the launcher restarted automatically.
    /// </summary>
    public bool AutoUpdate { get; set; } = true;

    /// <summary>Longest allowed delay for <see cref="RestartDelaySeconds"/>.</summary>
    public const int MaxRestartDelaySeconds = 300;

    /// <summary>
    /// Countdown (in seconds) shown before the launcher closes to install a downloaded update.
    /// 0 restarts immediately.
    /// </summary>
    public int RestartDelaySeconds { get; set; } = 5;

    /// <summary>Show apps as cards (grid) or rows (list).</summary>
    public AppViewMode ViewMode { get; set; } = AppViewMode.Grid;

    public AppSortMode SortMode { get; set; } = AppSortMode.NameAscending;
}

public enum AppViewMode
{
    Grid,
    List
}

public enum AppSortMode
{
    NameAscending,
    NameDescending,
    RecentlyUpdated
}
