using Avalonia.Controls;
using GameDock.Presentation.Views;

namespace GameDock.Presentation.Services;

public sealed class DialogService(IServiceProvider services) : IDialogService
{
    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, bool isDestructive = false)
    {
        var dialog = new ConfirmWindow(title, message, confirmText, isDestructive);
        return await ShowDialogAsync<bool>(dialog);
    }

    public Task ShowSettingsAsync() => ShowDialogAsync<bool>(services.GetRequiredService<SettingsWindow>());

    /// <summary>Shows <paramref name="dialog"/> modal to the active window (closing it without a result gives <c>default</c>).</summary>
    private static async Task<T?> ShowDialogAsync<T>(Window dialog)
    {
        var owner = WindowHelper.GetActiveWindow();
        if (owner is not null)
            return await dialog.ShowDialog<T?>(owner);

        // No window to be modal to (should not happen): show it on its own and wait until it closes.
        var closed = new TaskCompletionSource<T?>();
        dialog.Closed += (_, _) => closed.TrySetResult(default);
        dialog.Show();
        return await closed.Task;
    }
}
