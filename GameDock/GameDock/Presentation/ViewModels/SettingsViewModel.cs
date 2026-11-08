using System.ComponentModel;
using System.Globalization;
using GameDock.Presentation.Services;
using GameDock.Services.GitHub;
using GameDock.Services.Settings;
using GameDock.Services.Updates;

namespace GameDock.Presentation.ViewModels;

/// <summary>Settings dialog: Game Dock's own auto-update.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IAutoUpdater _autoUpdater;

    public SettingsViewModel(ISettingsService settingsService, IUpdateService updateService, IAutoUpdater autoUpdater)
    {
        _settingsService = settingsService;
        _autoUpdater = autoUpdater;

        var settings = settingsService.Current;
        AutoUpdate = settings.AutoUpdate;
        RestartDelayText = settings.RestartDelaySeconds.ToString(CultureInfo.InvariantCulture);
        CurrentVersionText = $"Current version: {VersionHelper.Format(updateService.CurrentVersion)}";
        IsNotInstalled = !updateService.IsInstalled;

        // The updater is a singleton that outlives this dialog: unsubscribed in OnClosed.
        _autoUpdater.PropertyChanged += OnAutoUpdaterPropertyChanged;
    }

    /// <summary>Asks the view to close: <c>true</c> = saved, <c>false</c> = cancelled.</summary>
    public event EventHandler<bool>? CloseRequested;

    /// <summary>Check GitHub for a new release of Game Dock on every start and install it automatically.</summary>
    [ObservableProperty]
    private bool _autoUpdate;

    /// <summary>Text of the "Seconds before auto-restart" box.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRestartDelayInvalid))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _restartDelayText = string.Empty;

    public string CurrentVersionText { get; }

    public int MaxRestartDelaySeconds => AppSettings.MaxRestartDelaySeconds;

    public bool IsRestartDelayInvalid => !TryGetRestartDelay(out _);

    /// <summary>Result of the last check, shown under the update buttons.</summary>
    public string? AvailableUpdateText => _autoUpdater.AvailableUpdate is { } update
        ? $"Version {VersionHelper.Format(update.Version)} is available."
        : null;

    public bool HasAvailableUpdate => _autoUpdater.AvailableUpdate is not null;

    /// <summary>True for development builds / unpacked copies, which are never updated.</summary>
    public bool IsNotInstalled { get; }

    public bool IsBusy => _autoUpdater.IsBusy;

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private Task CheckForUpdatesAsync() => _autoUpdater.CheckForUpdatesAsync();

    private bool CanCheckForUpdates() => !_autoUpdater.IsBusy;

    /// <summary>Enabled once "Check for updates" (or the startup check) found a newer version.</summary>
    [RelayCommand(CanExecute = nameof(CanDownloadAndUpdate))]
    private Task DownloadAndUpdateAsync() =>
        // Use the value in the box even if Settings was not saved yet.
        _autoUpdater.DownloadAndInstallAsync(TryGetRestartDelay(out var seconds) ? seconds : null);

    private bool CanDownloadAndUpdate() =>
        _autoUpdater.AvailableUpdate is not null && _autoUpdater.CanInstall && !_autoUpdater.IsBusy;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        TryGetRestartDelay(out var restartDelay);

        _settingsService.Save(_settingsService.Current with
        {
            AutoUpdate = AutoUpdate,
            RestartDelaySeconds = restartDelay
        });

        CloseRequested?.Invoke(this, true);
    }

    private bool CanSave() => !IsRestartDelayInvalid;

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    /// <summary>Called by the view when the window closes by any means (Cancel, Esc, Alt+F4, ×).</summary>
    public void OnClosed() => _autoUpdater.PropertyChanged -= OnAutoUpdaterPropertyChanged;

    private bool TryGetRestartDelay(out int seconds) =>
        int.TryParse(RestartDelayText?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out seconds)
        && seconds is >= 0 and <= AppSettings.MaxRestartDelaySeconds;

    private void OnAutoUpdaterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(AvailableUpdateText));
        OnPropertyChanged(nameof(HasAvailableUpdate));
        OnPropertyChanged(nameof(IsBusy));
        CheckForUpdatesCommand.NotifyCanExecuteChanged();
        DownloadAndUpdateCommand.NotifyCanExecuteChanged();
    }
}
