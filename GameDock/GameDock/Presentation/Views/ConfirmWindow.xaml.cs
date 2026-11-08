using System.Windows;

namespace GameDock.Presentation.Views;

/// <summary>Small themed confirmation dialog (e.g. "Remove GD Save Editor?").</summary>
public partial class ConfirmWindow : Window
{
    public ConfirmWindow(string title, string message, string confirmText, bool isDestructive)
    {
        InitializeComponent();

        Title = title;
        TitleText.Text = title.ToUpperInvariant();
        MessageText.Text = message;
        ConfirmButton.Content = confirmText.ToUpperInvariant();

        if (isDestructive)
            ConfirmButton.Style = (Style)FindResource("DangerButtonStyle");
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
