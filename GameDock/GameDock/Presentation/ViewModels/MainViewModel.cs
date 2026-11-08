using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using GameDock.Presentation.Services;
using GameDock.Presentation.Services.Toasts;
using GameDock.Services.Catalog;
using GameDock.Services.GitHub;
using GameDock.Services.Installation;
using GameDock.Services.Notifications;
using GameDock.Services.Settings;
using GameDock.Services.Updates;

namespace GameDock.Presentation.ViewModels;

/// <summary>Launcher window: the app cards, search / sort / view mode and the release check.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private static readonly TimeSpan ReleaseCheckTimeout = TimeSpan.FromSeconds(20);

    private readonly IGitHubReleaseClient _releaseClient;
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;
    private readonly IAutoUpdater _autoUpdater;
    private readonly IReadOnlyList<AppCardViewModel> _apps;
    private readonly DispatcherTimer _clock;

    private DateTimeOffset? _lastChecked;

    public MainViewModel(
        IGitHubReleaseClient releaseClient,
        IInstalledAppRegistry registry,
        IAppInstaller installer,
        ISettingsService settingsService,
        IUpdateService updateService,
        IDialogService dialogs,
        IToastService toasts,
        IAutoUpdater autoUpdater)
    {
        _releaseClient = releaseClient;
        _settingsService = settingsService;
        _dialogs = dialogs;
        _toasts = toasts;
        _autoUpdater = autoUpdater;

        _apps = AppCatalog.All
            .Select(app => new AppCardViewModel(app, installer, registry, toasts, dialogs))
            .ToList();

        foreach (var app in _apps)
        {
            app.InstalledStateChanged += (_, _) => ApplyView();
            app.PropertyChanged += OnAppPropertyChanged;
        }

        SortOptions =
        [
            new SortOption(AppSortMode.NameAscending, "Name (A–Z)"),
            new SortOption(AppSortMode.NameDescending, "Name (Z–A)"),
            new SortOption(AppSortMode.RecentlyUpdated, "Recently updated")
        ];

        // Restore the saved view without triggering the change handlers (which would save it again).
        var settings = settingsService.Current;
#pragma warning disable MVVMTK0034
        _selectedSort = SortOptions.FirstOrDefault(o => o.Mode == settings.SortMode) ?? SortOptions[0];
        _isListView = settings.ViewMode == AppViewMode.List;
#pragma warning restore MVVMTK0034

        LauncherVersionText = $"GAME DOCK V{VersionHelper.Format(updateService.CurrentVersion)}";

        // Keeps "Checked N min ago" current.
        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clock.Tick += (_, _) => OnPropertyChanged(nameof(LastCheckedText));
        _clock.Start();

        _autoUpdater.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IAutoUpdater.AvailableUpdate))
                OnPropertyChanged(nameof(HasLauncherUpdate));
        };

        RefreshInstalledState();
    }

    /// <summary>Installed apps, filtered and sorted.</summary>
    public ObservableCollection<AppCardViewModel> InstalledApps { get; } = [];

    /// <summary>Apps that are not installed yet, filtered and sorted.</summary>
    public ObservableCollection<AppCardViewModel> AvailableApps { get; } = [];

    public IReadOnlyList<SortOption> SortOptions { get; }

    [ObservableProperty]
    private SortOption _selectedSort;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGridView))]
    private bool _isListView;

    public bool IsGridView => !IsListView;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyPropertyChangedFor(nameof(LastCheckedText))]
    private bool _isRefreshing;

    public bool HasInstalledApps => InstalledApps.Count > 0;

    public bool HasAvailableApps => AvailableApps.Count > 0;

    public bool HasNoMatches => InstalledApps.Count == 0 && AvailableApps.Count == 0;

    public int UpdateCount => _apps.Count(a => a.IsUpdateAvailable);

    public bool HasUpdates => UpdateCount > 0;

    public string UpdateAllText => UpdateCount == 1 ? "UPDATE 1 APP" : $"UPDATE {UpdateCount} APPS";

    public bool HasLauncherUpdate => _autoUpdater.AvailableUpdate is not null;

    public string LauncherVersionText { get; }

    public Uri RepositoryUrl { get; } =
        new($"https://github.com/{GitHubUpdateService.Owner}/{GitHubUpdateService.Repository}");

    public string LastCheckedText
    {
        get
        {
            if (IsRefreshing)
                return "CHECKING FOR RELEASES…";
            if (_lastChecked is not { } checkedAt)
                return "NOT CHECKED YET";

            var elapsed = DateTimeOffset.Now - checkedAt;
            var ago = elapsed.TotalMinutes < 1 ? "JUST NOW"
                : elapsed.TotalHours < 1 ? $"{(int)elapsed.TotalMinutes} MIN AGO"
                : elapsed.TotalDays < 1 ? $"{(int)elapsed.TotalHours} H AGO"
                : $"{(int)elapsed.TotalDays} D AGO";

            return $"CHECKED {ago} · USING GITHUB RELEASES";
        }
    }

    /// <summary>Called once the window is shown: checks GitHub for the latest releases.</summary>
    public Task InitializeAsync() => RefreshAsync();

    /// <summary>Re-reads the registry (e.g. an app was installed or removed outside of Game Dock).</summary>
    public void RefreshInstalledState()
    {
        foreach (var app in _apps)
            app.RefreshInstalled();

        ApplyView();
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            using var timeout = new CancellationTokenSource(ReleaseCheckTimeout);
            var results = await Task.WhenAll(_apps.Select(app => FetchAsync(app.App, timeout.Token)));

            for (var i = 0; i < _apps.Count; i++)
                _apps[i].SetRelease(results[i].Release, results[i].Error);

            foreach (var app in _apps)
                app.RefreshInstalled();

            _lastChecked = DateTimeOffset.Now;

            var failed = results.FirstOrDefault(r => r.Error is not null);
            if (failed.Error is not null)
            {
                var toast = _toasts.Show("Could not check some releases", NotificationSeverity.Warning);
                toast.Details = failed.Error;
            }
        }
        finally
        {
            IsRefreshing = false;
            ApplyView();
        }
    }

    private bool CanRefresh() => !IsRefreshing;

    /// <summary>Updates every app that has a newer release, one after another.</summary>
    [RelayCommand]
    private async Task UpdateAllAsync()
    {
        foreach (var app in _apps.Where(a => a.IsUpdateAvailable).ToList())
        {
            if (app.UpdateCommand.CanExecute(null))
                await app.UpdateCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private void OpenSettings() => _dialogs.ShowSettings();

    [RelayCommand]
    private void ShowGrid() => IsListView = false;

    [RelayCommand]
    private void ShowList() => IsListView = true;

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private void OpenRepository()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = RepositoryUrl.AbsoluteUri, UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Debug.WriteLine($"Could not open {RepositoryUrl}: {ex.Message}");
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyView();

    partial void OnSelectedSortChanged(SortOption value)
    {
        ApplyView();
        SaveViewSettings();
    }

    partial void OnIsListViewChanged(bool value) => SaveViewSettings();

    private async Task<(ReleaseInfo? Release, string? Error)> FetchAsync(ManagedApp app, CancellationToken cancellationToken)
    {
        try
        {
            var release = await _releaseClient.GetLatestReleaseAsync(
                app.Owner, app.Repository, app.InstallerAssetPattern, cancellationToken);
            return (release, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or IOException)
        {
            Debug.WriteLine($"Release check of {app.Id} failed: {ex.Message}");
            var message = ex is OperationCanceledException ? "GitHub did not respond in time." : ex.Message;
            return (null, message);
        }
    }

    /// <summary>Rebuilds both sections from the current search text and sort order.</summary>
    private void ApplyView()
    {
        var query = SearchText.Trim();
        var visible = _apps.Where(a => query.Length == 0
                                       || a.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                                       || a.Tagline.Contains(query, StringComparison.OrdinalIgnoreCase)
                                       || a.Description.Contains(query, StringComparison.OrdinalIgnoreCase));

        visible = SelectedSort.Mode switch
        {
            AppSortMode.NameDescending => visible.OrderByDescending(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
            AppSortMode.RecentlyUpdated => visible
                .OrderByDescending(a => a.UpdatedAt ?? DateTimeOffset.MinValue)
                .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => visible.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
        };

        var list = visible.ToList();
        Sync(InstalledApps, list.Where(a => a.IsInstalled).ToList());
        Sync(AvailableApps, list.Where(a => !a.IsInstalled).ToList());

        OnPropertyChanged(nameof(HasInstalledApps));
        OnPropertyChanged(nameof(HasAvailableApps));
        OnPropertyChanged(nameof(HasNoMatches));
        OnUpdatesChanged();
    }

    /// <summary>Replaces the items only when they differ, so cards are not re-created needlessly.</summary>
    private static void Sync(ObservableCollection<AppCardViewModel> target, IReadOnlyList<AppCardViewModel> items)
    {
        if (target.SequenceEqual(items))
            return;

        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }

    private void OnAppPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppCardViewModel.IsUpdateAvailable))
            OnUpdatesChanged();
    }

    private void OnUpdatesChanged()
    {
        OnPropertyChanged(nameof(UpdateCount));
        OnPropertyChanged(nameof(HasUpdates));
        OnPropertyChanged(nameof(UpdateAllText));
    }

    private void SaveViewSettings()
    {
        var current = _settingsService.Current;
        var viewMode = IsListView ? AppViewMode.List : AppViewMode.Grid;
        if (current.ViewMode == viewMode && current.SortMode == SelectedSort.Mode)
            return;

        try
        {
            _settingsService.Save(current with { ViewMode = viewMode, SortMode = SelectedSort.Mode });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Remembering the view is a convenience; never fail because of it.
            Debug.WriteLine($"Could not save view settings: {ex.Message}");
        }
    }
}

/// <summary>An entry of the Sort combo box.</summary>
public sealed record SortOption(AppSortMode Mode, string Label)
{
    public override string ToString() => Label;
}
