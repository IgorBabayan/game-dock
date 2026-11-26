using Avalonia.Controls;
using GameDock.Presentation.Services;
using GameDock.Presentation.ViewModels;

namespace GameDock.Presentation.Views;

/// <summary>Settings dialog. Closes with <c>true</c> when the settings were saved.</summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        WindowChrome.Apply(this, titleBarHeight: 40);
        WindowChrome.EnableDrag(this, TitleBar);
        CloseButton.IsVisible = WindowChrome.HasCustomCaptionButtons;
        TitleText.Margin = TitleText.Margin + WindowChrome.TitleBarInset;

        DataContext = _viewModel = viewModel;

        _viewModel.CloseRequested += OnCloseRequested;
        Closed += OnClosed;
    }

    private void OnCloseRequested(object? sender, bool saved) => Close(saved);

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.CloseRequested -= OnCloseRequested;
        _viewModel.OnClosed();
    }
}
