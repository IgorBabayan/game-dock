using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace GameDock.Presentation.Services;

internal static class WindowHelper
{
    /// <summary>The currently active window, falling back to the main window. Used as dialog owner.</summary>
    public static Window? GetActiveWindow()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        foreach (var window in desktop.Windows)
        {
            if (window.IsActive)
                return window;
        }

        return desktop.MainWindow;
    }

    /// <summary>Closes the application (all windows), e.g. so an installer can replace its files.</summary>
    public static void ShutdownApplication()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
