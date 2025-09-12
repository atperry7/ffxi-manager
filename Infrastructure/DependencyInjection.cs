using System;
using Microsoft.Extensions.DependencyInjection;
using FFXIManager.Services;
using FFXIManager.Configuration;
using FFXIManager.ViewModels;
using FFXIManager;
using FFXIManager.ViewModels.CharacterMonitor;

namespace FFXIManager.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddAppServices(this IServiceCollection services)
        {
            // Core services
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<IConfigurationService, ConfigurationService>();
            services.AddSingleton<ILoggingService, LoggingService>();
            services.AddSingleton<ICachingService, CachingService>();
            services.AddSingleton<INotificationService, NotificationService>();
            services.AddSingleton<INotificationServiceEnhanced, NotificationServiceEnhanced>();
            services.AddSingleton<IValidationService, ValidationService>();

            // UI/Threading
            services.AddSingleton<IUiDispatcher, WpfUiDispatcher>();
            services.AddSingleton<IWindowEventTracker, WindowEventTracker>();

            // Process/Monitoring
            services.AddSingleton<IProcessUtilityService, ProcessUtilityService>();
            services.AddSingleton<IProcessManagementService, ProcessManagementService>();
            services.AddSingleton<IUnifiedMonitoringService, UnifiedMonitoringService>();
            services.AddSingleton<IPlayOnlineMonitorService, PlayOnlineMonitorService>();

            // App logic
            services.AddSingleton<IExternalApplicationService, ExternalApplicationService>();
            services.AddSingleton<IStatusMessageService, StatusMessageService>();
            services.AddSingleton<ICharacterOrderingService, CharacterOrderingService>();
            services.AddSingleton<IHotkeyMappingService, HotkeyMappingService>();
            services.AddSingleton<IHotkeyPerformanceMonitor, HotkeyPerformanceMonitor>();
            services.AddSingleton<IHotkeyActivationService, HotkeyActivationService>();
            services.AddSingleton<IProfileService>(sp =>
            {
                var config = sp.GetRequiredService<IConfigurationService>();
                var cache = sp.GetRequiredService<ICachingService>();
                var log = sp.GetRequiredService<ILoggingService>();
                var settingsService = sp.GetRequiredService<ISettingsService>();
                var settings = settingsService.LoadSettings();

                var profileService = new ProfileService(config, cache, log)
                {
                    PlayOnlineDirectory = settings.PlayOnlineDirectory,
                    SettingsService = settingsService
                };
                return profileService;
            });
            services.AddSingleton<IProfileOperationsService, ProfileOperationsService>();

            // UI Commanding / Dialogs
            services.AddSingleton<IUICommandService, UICommandService>();
            services.AddSingleton<IDialogService, DialogService>();

            // ViewModels and Views
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();
            services.AddSingleton<StatusBarViewModel>();
            services.AddSingleton<HeaderViewModel>();
            services.AddTransient<DiscoverySettingsViewModel>();
            services.AddTransient<CharacterMonitorViewModel>();
            services.AddTransient<CharacterCollectionViewModel>();
            services.AddTransient<CharacterMonitorWindowViewModel>();
            services.AddTransient<EmbeddedCharacterMonitorViewModel>();

            // Hotkey plumbing
            services.AddSingleton<IGlobalHotkeyService, LowLevelHotkeyService>();
            services.AddSingleton<ControllerInputService>();
            services.AddSingleton<GlobalHotkeyManager>();

            return services;
        }
    }
}




