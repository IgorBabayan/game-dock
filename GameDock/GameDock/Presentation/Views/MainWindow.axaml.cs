using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GameDock.Presentation.Services;
using GameDock.Presentation.ViewModels;

namespace GameDock.Presentation.Views;

/// <summary>Launcher window: title bar, toolbar, the app cards / rows and the status bar.</summary>
public partial class MainWindow : Window
{
    /// <summary>Window activation re-reads the installed apps at most this often (activation also follows every dialog).</summary>
    private static readonly TimeSpan ActivationRefreshInterval = TimeSpan.FromSeconds(2);

    private readonly MainViewModel _viewModel;
    private bool _initialized;
    private long _lastActivationRefresh;
    private ContextMenu? _appMenu;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;

        WindowChrome.Apply(this, titleBarHeight: 48);
        WindowChrome.EnableDrag(this, TitleBar);
        CaptionButtons.IsVisible = WindowChrome.HasCustomCaptionButtons;
        TitleContent.Margin = TitleContent.Margin + WindowChrome.TitleBarInset;

        Opened += OnOpened;
        Activated += OnActivated;

        // "…" buttons live in the item templates: one window-level handler serves all of them.
        AddHandler(Button.ClickEvent, OnButtonClick);

        // Ctrl+F (Cmd+F on macOS) focuses the search box; tunnel so the focused control cannot swallow it.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        _initialized = true;
        _lastActivationRefresh = Environment.TickCount64;
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            // The release check reports its own problems; nothing here may take the launcher down.
            System.Diagnostics.Debug.WriteLine($"Initialization failed: {ex}");
        }
    }

    /// <summary>An app may have been installed or removed outside of App Launcher while the window was in the background.</summary>
    private void OnActivated(object? sender, EventArgs e)
    {
        if (!_initialized)
            return;

        var now = Environment.TickCount64;
        if (now - _lastActivationRefresh < ActivationRefreshInterval.TotalMilliseconds)
            return;

        _lastActivationRefresh = now;
        _viewModel.RefreshInstalledState();
    }

    /// <summary>Opens the "…" menu of an app card or row below its button.</summary>
    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button button || !button.Classes.Contains("more"))
            return;

        _appMenu ??= this.TryFindResource("AppMenu", out var resource) ? resource as ContextMenu : null;
        if (_appMenu is null)
            return;

        _appMenu.DataContext = button.DataContext;
        _appMenu.PlacementTarget = button;
        _appMenu.Open(button);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F)
            return;

        var commandModifier = PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
        if (e.KeyModifiers != commandModifier)
            return;

        SearchBox.Focus();
        SearchBox.SelectAll();
        e.Handled = true;
    }

    #region Caption buttons

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeRestoreClick(object? sender, RoutedEventArgs e) => WindowChrome.ToggleMaximize(this);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    #endregion

    #region Maximized layout

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty || change.Property == OffScreenMarginProperty)
            UpdateMaximizedLayout();
    }

    /// <summary>
    /// A maximized window with an extended client area is larger than the screen by its (invisible) resize frame.
    /// Pad that away while maximized and drop the 1px window border, as Windows does. Also swaps Maximize / Restore.
    /// </summary>
    private void UpdateMaximizedLayout()
    {
        // Called from OnPropertyChanged, possibly before InitializeComponent has created the named elements.
        if (RootBorder is null || MaximizeButton is null || RestoreButton is null)
            return;

        var maximized = WindowState == WindowState.Maximized;

        RootBorder.BorderThickness = maximized ? default : new Thickness(1);
        RootBorder.Margin = maximized ? OffScreenMargin : default;

        MaximizeButton.IsVisible = !maximized;
        RestoreButton.IsVisible = maximized;
    }

    #endregion
}
