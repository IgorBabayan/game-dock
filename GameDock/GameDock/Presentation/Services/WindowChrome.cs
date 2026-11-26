using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;

namespace GameDock.Presentation.Services;

/// <summary>
/// Custom title bars on every platform:
/// <list type="bullet">
/// <item>Windows: the content extends into the title bar and draws its own Minimize / Maximize / Close buttons.</item>
/// <item>macOS: the content extends into the title bar, the native traffic lights stay at the top left.</item>
/// <item>Linux: the window manager keeps its own title bar (client-side decorations differ per desktop).</item>
/// </list>
/// </summary>
internal static class WindowChrome
{
    /// <summary>Room for the macOS traffic lights at the left of an extended title bar.</summary>
    private const double MacTrafficLightsWidth = 72;

    /// <summary>Whether the windows draw their own caption buttons.</summary>
    public static bool HasCustomCaptionButtons { get; } = OperatingSystem.IsWindows();

    /// <summary>Left padding the title bar content needs so it does not sit under native caption buttons.</summary>
    public static Thickness TitleBarInset { get; } =
        OperatingSystem.IsMacOS() ? new Thickness(MacTrafficLightsWidth, 0, 0, 0) : default;

    /// <summary>Extends the content into the title bar where the platform supports it well.</summary>
    public static void Apply(Window window, double titleBarHeight)
    {
        if (OperatingSystem.IsWindows())
        {
            window.ExtendClientAreaToDecorationsHint = true;
            window.ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
            window.ExtendClientAreaTitleBarHeightHint = titleBarHeight;
        }
        else if (OperatingSystem.IsMacOS())
        {
            window.ExtendClientAreaToDecorationsHint = true;
            window.ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;
            window.ExtendClientAreaTitleBarHeightHint = titleBarHeight;
        }
    }

    /// <summary>
    /// Makes <paramref name="titleBar"/> behave like a native caption: drag to move, double-click to maximize / restore.
    /// Buttons inside it handle their own clicks, so they never start a drag.
    /// </summary>
    public static void EnableDrag(Window window, Control titleBar)
    {
        titleBar.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(titleBar).Properties.IsLeftButtonPressed)
                return;

            if (e.ClickCount == 2)
            {
                if (window.CanResize)
                    ToggleMaximize(window);
            }
            else
            {
                window.BeginMoveDrag(e);
            }

            e.Handled = true;
        };
    }

    public static void ToggleMaximize(Window window) =>
        window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
