using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using FFXIManager.Services;
using FFXIManager.Services.AutoLogin;
using FFXIManager.Services.AutoLogin.Navigation;
using FFXIManager.Services.AutoLogin.ScreenDetection;
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
            // Configure Serilog as the logging provider
            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddSerilog(dispose: true);
                builder.SetMinimumLevel(LogLevel.Debug); // Allow all levels, let Serilog filter
            });

            // Core services
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<IConfigurationService, ConfigurationService>();
            services.AddSingleton<ILoggingService, LoggingService>();
            services.AddSingleton<ICachingService, CachingService>();
            services.AddSingleton<INotificationService, NotificationService>();
            services.AddSingleton<INotificationServiceEnhanced, NotificationServiceEnhanced>();
            services.AddSingleton<IValidationService, ValidationService>();
            services.AddSingleton<IWindowsCredentialsService, WindowsCredentialsService>();
            services.AddSingleton<IOTPService, OTPService>();

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
            services.AddSingleton<IPlayOnlineMemberAccountService, PlayOnlineMemberAccountService>();

            // Screen detection services for AutoLogin
            services.AddSingleton<IScreenshotCaptureService, ScreenshotCaptureService>();
            services.AddSingleton<IImageProcessor, ImageProcessor>();
            services.AddSingleton<IImageCropService, ImageCropService>();
            services.AddSingleton<ITemplateMatchingService, TemplateMatchingService>();
            services.AddSingleton<ITemplateManagementService, TemplateManagementService>();
            services.AddSingleton<IUIAutomationService, UIAutomationService>();

            // Context management service for AutoLogin
            services.AddSingleton<IAutoLoginContextService, AutoLoginContextService>();

            // AutoLogin support services (refactored for SOLID principles)
            services.AddSingleton<IWorkflowService, WorkflowService>();
            services.AddSingleton<WorkflowTaskBuilder>(); // Builds subtasks from workflows
            services.AddSingleton<IPlayOnlineNavigationService, Services.AutoLogin.Navigation.PlayOnlineNavigationService>();
            services.AddSingleton<IPlayOnlineAuthenticationService, PlayOnlineAuthenticationService>();

            // FFXI-specific support services (refactored for SOLID principles)
            services.AddSingleton<IFFXIScreenDetectionService, Services.AutoLogin.ScreenDetection.FFXIScreenDetectionService>();
            services.AddSingleton<IFFXIProcessDiscoveryService, FFXIProcessDiscoveryService>();
            services.AddSingleton<IWindowHandleManagementService, WindowHandleManagementService>();
            services.AddSingleton<ICharacterNavigationService, Services.AutoLogin.Navigation.CharacterNavigationService>();

            // Windower-specific support services (refactored for SOLID principles)
            services.AddSingleton<IProcessLaunchService, ProcessLaunchService>();
            services.AddSingleton<IMonitorPositioningService, MonitorPositioningService>();

            // Auto-login handlers
            services.AddSingleton<ILoginTaskHandler, POLProxyLaunchHandler>();
            services.AddSingleton<ILoginTaskHandler, WindowerLaunchHandler>();
            services.AddSingleton<ILoginTaskHandler, PlayOnlineAuthHandler>();
            services.AddSingleton<ILoginTaskHandler, FFXIGameHandler>();
            services.AddSingleton<ILoginTaskHandler, DynamicWorkflowHandler>(); // Dynamic handler for data-driven workflows

            // Diagnostics UI (Template Navigation Tuner)
            services.AddTransient<FFXIManager.Views.TemplateNavigationTuner>();
            services.AddSingleton<ILoginTaskHandlerResolver, LoginTaskHandlerResolver>();

            // Auto-login queue services (refactored for SOLID principles)
            services.AddSingleton<IQueueCollectionManager, QueueCollectionManager>();
            services.AddSingleton<IQueueStateMachine, QueueStateMachine>();
            services.AddSingleton<IQueuePersistenceService, QueuePersistenceService>();
            services.AddSingleton<IQueueStatisticsService, QueueStatisticsService>();
            services.AddSingleton<IQueueExecutionOrchestrator, QueueExecutionOrchestrator>();
            services.AddSingleton<IAutoLoginTaskExecutor, AutoLoginTaskExecutor>();
            services.AddSingleton<IAutoLoginQueueService, AutoLoginQueueService>();
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
            services.AddSingleton<AutoLoginQueueViewModel>();
            services.AddTransient<WorkflowEditorViewModel>();
            services.AddTransient<FFXIManager.Views.WorkflowEditor>();
            services.AddTransient<TemplateViewerDialogViewModel>();
            services.AddTransient<FFXIManager.Views.TemplateViewerDialog>();

            // Hotkey plumbing
            services.AddSingleton<IGlobalHotkeyService, LowLevelHotkeyService>();
            services.AddSingleton<ControllerInputService>();
            services.AddSingleton<GlobalHotkeyManager>();

            return services;
        }
    }
}




