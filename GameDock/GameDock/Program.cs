using Avalonia;

namespace GameDock;

internal static class Program
{
    /// <summary>
    /// Entry point. Don't use any Avalonia, third-party APIs or any SynchronizationContext-reliant code before
    /// AppMain is called: things aren't initialized yet and stuff might break.
    /// </summary>
    [STAThread]
    public static int Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>Avalonia configuration; also used by the visual designer.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Bundled UI font, so the launcher looks the same on Linux and macOS (Segoe UI is used where present).
            .WithInterFont()
            .LogToTrace();
}
