namespace GameDock.Presentation.Services;

/// <summary>Opens the launcher's dialogs. Must be used from the UI thread.</summary>
public interface IDialogService
{
    /// <summary>Shows a Yes/No style confirmation.</summary>
    /// <returns><c>true</c> if the user pressed the confirm button.</returns>
    bool Confirm(string title, string message, string confirmText, bool isDestructive = false);

    /// <summary>Opens the Settings dialog.</summary>
    void ShowSettings();
}
