using GameDock.Presentation.Services;
using GameDock.Presentation.Services.Toasts;
using GameDock.Presentation.ViewModels;
using GameDock.Presentation.Views;
using GameDock.Services.GitHub;
using GameDock.Services.Installation;
using GameDock.Services.Settings;
using GameDock.Services.Updates;

namespace GameDock.Extensions;

static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddApplicationServices() => services
            .AddInfrastructure()
            .AddPresentation();

        private IServiceCollection AddInfrastructure()
        {
            services.AddSingleton<ISettingsService, JsonSettingsService>();
            services.AddSingleton<IGitHubReleaseClient, GitHubReleaseClient>();

            // Game Dock's own updates
            services.AddSingleton<IUpdateService, GitHubUpdateService>();

            // Managed apps (Inno Setup installers registered under ...\Uninstall\{AppId}_is1)
            services.AddSingleton<IInstalledAppRegistry, InnoSetupRegistry>();
            services.AddSingleton<IAppInstaller, InnoSetupInstaller>();

            return services;
        }

        private IServiceCollection AddPresentation()
        {
            // Services
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<IToastService, ToastService>();
            services.AddSingleton<IAutoUpdater, AutoUpdater>();

            // Shell
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();

            // Settings (new instance every time the dialog opens)
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<SettingsWindow>();

            return services;
        }
    }
}
