using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameDock.Presentation.Services;
using GameDock.Presentation.Services.Toasts;
using GameDock.Services.Catalog;
using GameDock.Services.GitHub;
using GameDock.Services.Installation;
using GameDock.Services.Notifications;

namespace GameDock.Presentation.ViewModels;

/// <summary>One app on the launcher: its installed state, latest release and the Install / Update / Remove / Open actions.</summary>
public sealed partial class AppCardViewModel : ObservableObject
{
    private readonly IAppInstaller _installer;
    private readonly IInstalledAppRegistry _registry;
    private readonly IToastService _toasts;
    private readonly IDialogService _dialogs;

    private CancellationTokenSource? _operation;

    public AppCardViewModel(
        ManagedApp app,
        IAppInstaller installer,
        IInstalledAppRegistry registry,
        IToastService toasts,
        IDialogService dialogs)
    {
        App = app;
        _installer = installer;
        _registry = registry;
        _toasts = toasts;
        _dialogs = dialogs;

        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(app.AccentColor)!;
        brush.Freeze();
        AccentBrush = brush;

        Icon = LoadIcon(app.IconUri);
    }

    /// <summary>Raised when the app was installed or removed, so the launcher can move it to the other section.</summary>
    public event EventHandler? InstalledStateChanged;

    public ManagedApp App { get; }

    public string Name => App.Name;
    public string NameAccent => App.NameAccent;
    public string NameRest => App.NameRest;
    public string Tagline => App.Tagline;
    public string Description => App.Description;
    public Brush AccentBrush { get; }
    public ImageSource? Icon { get; }

    // ===== State =====

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstalled), nameof(IsUpdateAvailable), nameof(VersionText), nameof(StatusText),
        nameof(HasStatus), nameof(PrimaryText), nameof(InstalledVersionText), nameof(UpdateText))]
    private InstalledApp? _installed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateAvailable), nameof(VersionText), nameof(StatusText), nameof(HasStatus),
        nameof(PrimaryText), nameof(LastUpdatedText), nameof(UpdatedAt), nameof(CanInstall), nameof(UpdateText))]
    private ReleaseInfo? _latest;

    /// <summary>Why the latest release could not be read (offline, rate limit...), or <c>null</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(HasStatus), nameof(LastUpdatedText))]
    private string? _releaseError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryText), nameof(HasStatus))]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyText = string.Empty;

    /// <summary>Download progress 0..100, or <c>null</c> for an indeterminate bar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProgressIndeterminate))]
    private double? _progress;

    [ObservableProperty]
    private bool _canCancel;

    public bool IsInstalled => Installed is not null;

    public bool CanInstall => Latest?.Installer is not null;

    public bool IsUpdateAvailable =>
        Installed?.Version is { } installed && Latest is { Installer: not null } latest && latest.Version > installed;

    public bool IsProgressIndeterminate => Progress is null;

    /// <summary>Version shown in the card header: the installed one, or the latest release when not installed.</summary>
    public string VersionText =>
        Installed is { } installed
            ? installed.Version is { } v ? $"V{VersionHelper.Format(v)}" : "INSTALLED"
            : Latest is { } latest ? $"V{VersionHelper.Format(latest.Version)}" : "—";

    public string? InstalledVersionText =>
        Installed?.Version is { } v ? VersionHelper.Format(v) : null;

    public string? UpdateText =>
        IsUpdateAvailable ? $"UPDATE TO V{VersionHelper.Format(Latest!.Version)}" : null;

    /// <summary>Status chip under the description.</summary>
    public string? StatusText
    {
        get
        {
            if (IsUpdateAvailable)
                return $"UPDATE AVAILABLE · V{VersionHelper.Format(Latest!.Version)}";
            if (IsInstalled)
                return null;
            if (CanInstall)
                return "AVAILABLE TO INSTALL";
            if (ReleaseError is not null)
                return "RELEASE INFO UNAVAILABLE";
            return Latest is null ? "NO RELEASE YET" : "NO WINDOWS INSTALLER";
        }
    }

    public bool HasStatus => !IsBusy && StatusText is not null;

    public DateTimeOffset? UpdatedAt => Latest?.PublishedAt;

    public string LastUpdatedText =>
        Latest?.PublishedAt is { } published
            ? $"Last updated: {published.ToLocalTime():MMM d, yyyy}"
            : ReleaseError is not null ? "Last updated: unknown" : "Last updated: —";

    /// <summary>Text of the main button.</summary>
    public string PrimaryText => IsUpdateAvailable ? "UPDATE" : IsInstalled ? "OPEN" : "INSTALL";

    // ===== Refresh =====

    /// <summary>Re-reads the installed state from the registry (cheap; also called when the window is activated).</summary>
    public void RefreshInstalled()
    {
        if (IsBusy)
            return;

        var wasInstalled = IsInstalled;
        Installed = _registry.Find(App);
        RefreshCommands();

        if (wasInstalled != IsInstalled)
            InstalledStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetRelease(ReleaseInfo? release, string? error)
    {
        // Keep the last known release if this check failed.
        if (error is null)
            Latest = release;

        ReleaseError = error;
        RefreshCommands();
    }

    // ===== Commands =====

    /// <summary>Main button: Update when a newer version exists, otherwise Open or Install.</summary>
    [RelayCommand(CanExecute = nameof(CanRunPrimary))]
    private Task PrimaryAsync()
    {
        if (IsUpdateAvailable)
            return UpdateAsync();

        if (IsInstalled)
        {
            Open();
            return Task.CompletedTask;
        }

        return InstallAsync();
    }

    private bool CanRunPrimary() => !IsBusy && (IsInstalled || CanInstall);

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void Open()
    {
        if (Installed is not { } installed)
            return;

        try
        {
            _installer.Launch(App, installed);
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            ShowError($"Could not open {Name}", ex.Message);
            RefreshInstalled();
        }
    }

    private bool CanOpen() => !IsBusy && IsInstalled;

    [RelayCommand(CanExecute = nameof(CanInstallNow))]
    private Task InstallAsync() => RunInstallerAsync(isUpdate: false);

    private bool CanInstallNow() => !IsBusy && !IsInstalled && CanInstall;

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private Task UpdateAsync() => RunInstallerAsync(isUpdate: true);

    private bool CanUpdate() => !IsBusy && IsUpdateAvailable;

    /// <summary>Installs the latest release over the current one (repairs a broken install).</summary>
    [RelayCommand(CanExecute = nameof(CanReinstall))]
    private Task ReinstallAsync() => RunInstallerAsync(isUpdate: true);

    private bool CanReinstall() => !IsBusy && IsInstalled && CanInstall;

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task RemoveAsync()
    {
        if (Installed is not { } installed)
            return;

        if (_installer.IsRunning(App, installed))
        {
            _toasts.Show($"Close {Name} before removing it", NotificationSeverity.Warning);
            return;
        }

        if (!_dialogs.Confirm(
                $"Remove {Name}?",
                $"{Name} will be uninstalled from\n{installed.InstallFolder}\n\nYour saves and settings are not deleted.",
                "Remove",
                isDestructive: true))
            return;

        BeginOperation("Removing…", canCancel: false);
        try
        {
            await _installer.UninstallAsync(App, installed);
            _toasts.Show($"{Name} was removed", NotificationSeverity.Success);
        }
        catch (Exception ex) when (IsOperationError(ex))
        {
            Debug.WriteLine($"Remove {App.Id} failed: {ex}");
            ShowError($"Could not remove {Name}", ex.Message);
        }
        finally
        {
            EndOperation();
        }
    }

    private bool CanRemove() => !IsBusy && IsInstalled;

    [RelayCommand(CanExecute = nameof(CanCancelOperation))]
    private void Cancel() => _operation?.Cancel();

    private bool CanCancelOperation() => IsBusy && CanCancel;

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void OpenFolder()
    {
        if (Installed is { } installed)
            OpenShell(installed.InstallFolder);
    }

    [RelayCommand]
    private void OpenGitHub() => OpenShell(App.RepositoryUrl.AbsoluteUri);

    [RelayCommand]
    private void OpenReleases() => OpenShell((Latest?.PageUrl ?? App.ReleasesUrl).AbsoluteUri);

    // ===== Install / update =====

    private async Task RunInstallerAsync(bool isUpdate)
    {
        if (Latest is not { Installer: not null } release)
        {
            _toasts.Show($"No Windows installer is published for {Name} yet", NotificationSeverity.Warning);
            return;
        }

        var current = Installed;
        var version = VersionHelper.Format(release.Version);
        var operation = BeginOperation("Waiting…", canCancel: true);

        // The installer reports stages from a background thread: marshal them to the UI thread.
        var ui = SynchronizationContext.Current;
        void OnStage(InstallStage stage) => Post(ui, () => ApplyStage(stage, version));

        // Progress<T> is created on the UI thread, so reports arrive on the UI thread.
        var progress = new System.Progress<DownloadProgress>(p =>
        {
            if (!IsBusy || !CanCancel)
                return;

            Progress = p.Fraction is { } fraction ? fraction * 100 : null;
            BusyText = p.TotalBytes > 0
                ? $"Downloading v{version} · {ToMegabytes(p.BytesReceived):0.0} / {ToMegabytes(p.TotalBytes):0.0} MB"
                : $"Downloading v{version} · {ToMegabytes(p.BytesReceived):0.0} MB";
        });

        try
        {
            await _installer.InstallAsync(App, release, current, progress, OnStage, operation.Token);

            _toasts.Show(
                current is null ? $"{Name} {version} installed" : $"{Name} updated to {version}",
                NotificationSeverity.Success);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            _toasts.Show(isUpdate ? $"Update of {Name} cancelled" : $"Installation of {Name} cancelled");
        }
        catch (Exception ex) when (IsOperationError(ex))
        {
            Debug.WriteLine($"Install {App.Id} failed: {ex}");
            ShowError(isUpdate ? $"Could not update {Name}" : $"Could not install {Name}", ex.Message);
        }
        finally
        {
            EndOperation();
        }
    }

    private void ApplyStage(InstallStage stage, string version)
    {
        if (!IsBusy)
            return;

        switch (stage)
        {
            case InstallStage.Waiting:
                BusyText = "Waiting for another operation…";
                Progress = null;
                break;
            case InstallStage.Downloading:
                BusyText = $"Downloading v{version}…";
                Progress = 0;
                break;
            case InstallStage.Installing:
                BusyText = $"Installing v{version}…";
                Progress = null;
                CanCancel = false;
                CancelCommand.NotifyCanExecuteChanged();
                break;
            case InstallStage.Removing:
                BusyText = "Removing…";
                Progress = null;
                break;
        }
    }

    private CancellationTokenSource BeginOperation(string text, bool canCancel)
    {
        _operation?.Dispose();
        _operation = new CancellationTokenSource();

        BusyText = text;
        Progress = null;
        CanCancel = canCancel;
        IsBusy = true;
        RefreshCommands();

        return _operation;
    }

    private void EndOperation()
    {
        IsBusy = false;
        CanCancel = false;
        Progress = null;
        BusyText = string.Empty;

        _operation?.Dispose();
        _operation = null;

        RefreshInstalled();
        // Installed may not have changed (failed update), but the version may have.
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasStatus));
        InstalledStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshCommands()
    {
        PrimaryCommand.NotifyCanExecuteChanged();
        OpenCommand.NotifyCanExecuteChanged();
        InstallCommand.NotifyCanExecuteChanged();
        UpdateCommand.NotifyCanExecuteChanged();
        ReinstallCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
    }

    private void ShowError(string message, string details)
    {
        var toast = _toasts.Show(message, NotificationSeverity.Error, TimeSpan.FromSeconds(10));
        toast.Details = details;
    }

    private void OpenShell(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ShowError("Could not open the link", ex.Message);
        }
    }

    private static void Post(SynchronizationContext? context, Action action)
    {
        if (context is null)
            action();
        else
            context.Post(_ => action(), null);
    }

    private static ImageSource? LoadIcon(string uri)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(uri, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
        {
            Debug.WriteLine($"Could not load icon {uri}: {ex.Message}");
            return null;
        }
    }

    private static bool IsOperationError(Exception ex) =>
        ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException
            or Win32Exception or InstallerFailedException or OperationCanceledException or InvalidOperationException;

    private static double ToMegabytes(long bytes) => bytes / 1024d / 1024d;
}
