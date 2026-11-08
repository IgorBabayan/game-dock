namespace GameDock.Services.Settings;

/// <summary>User preferences persisted between application runs.</summary>
public sealed record AppSettings
{
    /// <summary>
    /// When enabled, the latest GitHub release of Game Dock is checked on every start and, if newer,
    /// downloaded, installed and the launcher restarted automatically.
    /// </summary>
    public bool AutoUpdate { get; init; } = true;

    /// <summary>Longest allowed delay for <see cref="RestartDelaySeconds"/>.</summary>
    public const int MaxRestartDelaySeconds = 300;

    /// <summary>
    /// Countdown (in seconds) shown before the launcher closes to install a downloaded update.
    /// 0 restarts immediately.
    /// </summary>
    public int RestartDelaySeconds { get; init; } = 5;

    /// <summary>Show apps as cards (grid) or rows (list).</summary>
    public AppViewMode ViewMode { get; init; } = AppViewMode.Grid;

    public AppSortMode SortMode { get; init; } = AppSortMode.NameAscending;
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
