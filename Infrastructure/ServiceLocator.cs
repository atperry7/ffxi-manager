using FFXIManager.Services;
using FFXIManager.Configuration;

namespace FFXIManager.Infrastructure
{
    /// <summary>
    /// Enhanced service locator with external application management services
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
                _loggingService ??= new LoggingService();
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
        
        public static IExternalApplicationService ExternalApplicationService
        {
            get
            {
                _externalApplicationService ??= new ExternalApplicationService(LoggingService, SettingsService);
                return _externalApplicationService;
            }
        }

        public static IPlayOnlineMonitorService PlayOnlineMonitorService
        {
            get
            {
                _playOnlineMonitorService ??= new PlayOnlineMonitorService(LoggingService);
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
            IPlayOnlineMonitorService? playOnlineMonitorService = null)
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
        }
    }
}