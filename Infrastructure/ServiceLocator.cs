using FFXIManager.Services;
using FFXIManager.Configuration;

namespace FFXIManager.Infrastructure
{
    /// <summary>
    /// Simple service locator without complex unified dependencies
    /// </summary>
    public static class ServiceLocator
    {
        private static ISettingsService? _settingsService;
        private static IProfileService? _profileService;
        private static IProfileOperationsService? _profileOperationsService;
        private static IStatusMessageService? _statusMessageService;
        private static IConfigurationService? _configurationService;
        private static IValidationService? _validationService;
        private static ILoggingService? _loggingService;
        private static ICachingService? _cachingService;
        private static INotificationService? _notificationService;
        private static IExternalApplicationService? _externalApplicationService;
        private static IPlayOnlineMonitorService? _playOnlineMonitorService;
        private static IProcessManagementService? _processManagementService;
        private static IProcessUtilityService? _processUtilityService;
        private static IUnifiedMonitoringService? _unifiedMonitoringService;
        private static IUiDispatcher? _uiDispatcher;
        
        public static ISettingsService SettingsService 
        {
            get
            {
                _settingsService ??= new SettingsService();
                return _settingsService;
            }
        }

        public static IConfigurationService ConfigurationService
        {
            get
            {
                _configurationService ??= new ConfigurationService();
                return _configurationService;
            }
        }

        public static ILoggingService LoggingService
        {
            get
            {
                _loggingService ??= new LoggingService(SettingsService);
                return _loggingService;
            }
        }

        public static ICachingService CachingService
        {
            get
            {
                _cachingService ??= new CachingService();
                return _cachingService;
            }
        }

        public static INotificationService NotificationService
        {
            get
            {
                _notificationService ??= new NotificationService(LoggingService);
                return _notificationService;
            }
        }

        public static IValidationService ValidationService
        {
            get
            {
                _validationService ??= new ValidationService(ConfigurationService);
                return _validationService;
            }
        }

        public static IProcessManagementService ProcessManagementService
        {
            get
            {
                _processManagementService ??= new ProcessManagementService(LoggingService, UiDispatcher);
                return _processManagementService;
            }
        }

        public static IProcessUtilityService ProcessUtilityService
        {
            get
            {
                _processUtilityService ??= new ProcessUtilityService(LoggingService);
                return _processUtilityService;
            }
        }

        public static IUiDispatcher UiDispatcher
        {
            get
            {
                _uiDispatcher ??= new WpfUiDispatcher();
                return _uiDispatcher;
            }
        }
        
        public static IExternalApplicationService ExternalApplicationService
        {
            get
            {
                _externalApplicationService ??= new ExternalApplicationService(
                    UnifiedMonitoringService,
                    LoggingService,
                    SettingsService);
                return _externalApplicationService;
            }
        }

        public static IUnifiedMonitoringService UnifiedMonitoringService
        {
            get
            {
                _unifiedMonitoringService ??= new UnifiedMonitoringService(ProcessUtilityService, LoggingService, UiDispatcher);
                return _unifiedMonitoringService;
            }
        }
        
        public static IPlayOnlineMonitorService PlayOnlineMonitorService
        {
            get
            {
                _playOnlineMonitorService ??= new PlayOnlineMonitorService(
                    UnifiedMonitoringService,
                    LoggingService,
                    UiDispatcher);
                return _playOnlineMonitorService;
            }
        }
        
        public static IProfileService ProfileService
        {
            get
            {
                if (_profileService == null)
                {
                    var settings = SettingsService.LoadSettings();
                    _profileService = new ProfileService(ConfigurationService, CachingService, LoggingService)
                    {
                        PlayOnlineDirectory = settings.PlayOnlineDirectory,
                        SettingsService = SettingsService
                    };
                }
                return _profileService;
            }
        }
        
        public static IProfileOperationsService ProfileOperationsService
        {
            get
            {
                _profileOperationsService ??= new ProfileOperationsService(ProfileService, SettingsService);
                return _profileOperationsService;
            }
        }
        
        public static IStatusMessageService StatusMessageService
        {
            get
            {
                _statusMessageService ??= new StatusMessageService();
                return _statusMessageService;
            }
        }
        
        // For testing - allow injection of mock services
        public static void Configure(
            ISettingsService? settingsService = null, 
            IProfileService? profileService = null,
            IProfileOperationsService? profileOperationsService = null,
            IStatusMessageService? statusMessageService = null,
            IConfigurationService? configurationService = null,
            IValidationService? validationService = null,
            ILoggingService? loggingService = null,
            ICachingService? cachingService = null,
            INotificationService? notificationService = null,
            IExternalApplicationService? externalApplicationService = null,
            IPlayOnlineMonitorService? playOnlineMonitorService = null,
            IProcessManagementService? processManagementService = null,
            IUiDispatcher? uiDispatcher = null)
        {
            _settingsService = settingsService;
            _profileService = profileService;
            _profileOperationsService = profileOperationsService;
            _statusMessageService = statusMessageService;
            _configurationService = configurationService;
            _validationService = validationService;
            _loggingService = loggingService;
            _cachingService = cachingService;
            _notificationService = notificationService;
            _externalApplicationService = externalApplicationService;
            _playOnlineMonitorService = playOnlineMonitorService;
            _processManagementService = processManagementService;
            _uiDispatcher = uiDispatcher;
        }
        
        // For cleanup during testing
        public static void Reset()
        {
            _settingsService = null;
            _profileService = null;
            _profileOperationsService = null;
            _statusMessageService = null;
            _configurationService = null;
            _validationService = null;
            _loggingService = null;
            _cachingService = null;
            _notificationService = null;
            _externalApplicationService = null;
            _playOnlineMonitorService = null;
            _processManagementService = null;
            _uiDispatcher = null;
        }
    }
}
