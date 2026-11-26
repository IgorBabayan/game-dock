using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using GameDock.Presentation.Services;

namespace GameDock.Presentation.Views;

/// <summary>Small themed confirmation dialog (e.g. "Remove GD Save Editor?"). Closes with <c>true</c> when confirmed.</summary>
public partial class ConfirmWindow : Window
{
    public ConfirmWindow(string title, string message, string confirmText, bool isDestructive)
    {
        InitializeComponent();
        WindowChrome.Apply(this, titleBarHeight: 40);
        WindowChrome.EnableDrag(this, TitleBar);
        CloseButton.IsVisible = WindowChrome.HasCustomCaptionButtons;
        TitleText.Margin = TitleText.Margin + WindowChrome.TitleBarInset;

        Title = title;
        TitleText.Text = title.ToUpperInvariant();
        MessageText.Text = message;
        ConfirmButton.Content = confirmText.ToUpperInvariant();

        if (isDestructive && this.TryFindResource("DangerButton", out var theme) && theme is ControlTheme danger)
            ConfirmButton.Theme = danger;
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
