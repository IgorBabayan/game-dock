using GameDock.Presentation.Views;

namespace GameDock.Presentation.Services;

public sealed class DialogService(IServiceProvider services) : IDialogService
{
    public bool Confirm(string title, string message, string confirmText, bool isDestructive = false)
    {
        var dialog = new ConfirmWindow(title, message, confirmText, isDestructive)
        {
            Owner = WindowHelper.GetActiveWindow()
        };

        return dialog.ShowDialog() == true;
    }

    public void ShowSettings()
    {
        var dialog = services.GetRequiredService<SettingsWindow>();
        dialog.Owner = WindowHelper.GetActiveWindow();
        dialog.ShowDialog();
    }
}
