using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using GameDock.Presentation.ViewModels;

namespace GameDock.Presentation.Views;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _initialized;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;

        Loaded += OnLoaded;
        Activated += OnActivated;
        StateChanged += (_, _) => UpdateMaximizedLayout();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _initialized = true;
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

    /// <summary>An app may have been installed or removed outside of Game Dock while the window was in the background.</summary>
    private void OnActivated(object? sender, EventArgs e)
    {
        if (_initialized)
            _viewModel.RefreshInstalledState();
    }

    /// <summary>Opens the "…" menu of an app card or row below its button.</summary>
    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || FindResource("AppMenu") is not ContextMenu menu)
            return;

        menu.DataContext = button.DataContext;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void OnFindExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    #region Caption buttons

    private void OnMinimizeExecuted(object sender, ExecutedRoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaximizeExecuted(object sender, ExecutedRoutedEventArgs e) => SystemCommands.MaximizeWindow(this);

    private void OnRestoreExecuted(object sender, ExecutedRoutedEventArgs e) => SystemCommands.RestoreWindow(this);

    private void OnCloseExecuted(object sender, ExecutedRoutedEventArgs e) => SystemCommands.CloseWindow(this);

    #endregion

    #region Maximized layout

    private const int SM_CXSIZEFRAME = 32;
    private const int SM_CYSIZEFRAME = 33;
    private const int SM_CXPADDEDBORDER = 92;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    /// <summary>
    /// A maximized window is larger than the screen by its (invisible) resize frame on every side. With the native
    /// title bar removed, that frame would cut off the edges of the content: pad it away while maximized, and drop the
    /// 1px window border, as Windows does. Also swaps the Maximize / Restore caption buttons.
    /// </summary>
    private void UpdateMaximizedLayout()
    {
        var maximized = WindowState == WindowState.Maximized;

        if (maximized)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var padded = GetSystemMetrics(SM_CXPADDEDBORDER);
            var x = (GetSystemMetrics(SM_CXSIZEFRAME) + padded) / dpi.DpiScaleX;
            var y = (GetSystemMetrics(SM_CYSIZEFRAME) + padded) / dpi.DpiScaleY;

            RootBorder.BorderThickness = new Thickness(0);
            RootBorder.Margin = new Thickness(x, y, x, y);
        }
        else
        {
            RootBorder.BorderThickness = new Thickness(1);
            RootBorder.Margin = new Thickness(0);
        }

        MaximizeButton.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        RestoreButton.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;
    }

    #endregion
}
