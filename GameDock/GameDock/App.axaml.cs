using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GameDock.Extensions;
using GameDock.Presentation.Services;
using GameDock.Presentation.Views;

namespace GameDock;

/// <summary>
/// Composition root: builds the DI container and creates the main window from it.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    /// <summary>Root service provider. Prefer constructor injection; use this only where XAML creates objects for you.</summary>
    public static IServiceProvider Services =>
        (Current as App)?._services ?? throw new InvalidOperationException("Services are not initialized yet.");

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        _services = new ServiceCollection()
            .AddApplicationServices()
            .BuildServiceProvider(new ServiceProviderOptions
            {
#if DEBUG
                // Walks every registration on build: useful while developing, pure startup cost in release builds.
                ValidateOnBuild = true,
                ValidateScopes = true
#endif
            });

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = _services.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;
            desktop.Exit += (_, _) => _services.Dispose();

            // Auto-update runs in the background once the window is on screen and reports through toasts:
            // download progress, then a countdown before the launcher restarts into the new version.
            mainWindow.Opened += OnMainWindowOpened;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnMainWindowOpened(object? sender, EventArgs e)
    {
        if (sender is MainWindow window)
            window.Opened -= OnMainWindowOpened;

        if (_services is not null)
            _ = RunAutoUpdateAsync(_services);
    }

    private static async Task RunAutoUpdateAsync(IServiceProvider services)
    {
        try
        {
            await services.GetRequiredService<IAutoUpdater>().RunOnStartupAsync();
        }
        catch (Exception ex)
        {
            // An update problem must never take the launcher down.
            System.Diagnostics.Debug.WriteLine($"Auto-update failed: {ex}");
        }
    }
}
