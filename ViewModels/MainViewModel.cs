using FFXIManager.Configuration;
using FFXIManager.Infrastructure;
using FFXIManager.Services;
using FFXIManager.ViewModels.Base;
using FFXIManager.Views;

namespace FFXIManager.ViewModels
{
    /// <summary>
    /// Clean, refactored Main ViewModel that coordinates between specialized ViewModels
    /// Following Single Responsibility Principle and proper separation of concerns
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private readonly IStatusMessageService _statusService;
        private readonly IConfigurationService _configService;

        public MainViewModel() : this(
            ServiceLocator.SettingsService,
            ServiceLocator.ProfileService,
            ServiceLocator.ProfileOperationsService,
            ServiceLocator.StatusMessageService,
            new UICommandService(),
            new DialogService(),
            ServiceLocator.ConfigurationService,
            ServiceLocator.ValidationService,
            ServiceLocator.LoggingService,
            ServiceLocator.NotificationService,
            ServiceLocator.ExternalApplicationService)
        {
        }

        public MainViewModel(
            ISettingsService settingsService,
            IProfileService profileService,
            IProfileOperationsService profileOperations,
            IStatusMessageService statusService,
            IUICommandService uiCommandService,
            IDialogService dialogService,
            IConfigurationService configService,
            IValidationService validationService,
            ILoggingService loggingService,
            INotificationService notificationService,
            IExternalApplicationService applicationService)
        {
            _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));

            // Create specialized ViewModels with their specific dependencies
            ProfileManagement = new ProfileManagementViewModel(
                profileOperations, statusService, settingsService, 
                profileService, dialogService, validationService);

            ApplicationManagement = new ApplicationManagementViewModel(
                applicationService, statusService, loggingService);

            UICommands = new UICommandsViewModel(uiCommandService, statusService);

            // Subscribe to status message changes
            _statusService.MessageChanged += (_, message) => OnPropertyChanged(nameof(StatusMessage));

            // Subscribe to IsLoading changes from ProfileManagement
            ProfileManagement.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ProfileManagement.IsLoading))
                {
                    OnPropertyChanged(nameof(IsLoading));
                }
            };

            // Initialize commands and data
            InitializeCommands();
            InitializeAsync();
        }

        #region Properties

        /// <summary>
        /// Profile Management ViewModel - handles all profile-related operations
        /// </summary>
        public ProfileManagementViewModel ProfileManagement { get; }

        /// <summary>
        /// Application Management ViewModel - handles all external application operations
        /// </summary>
        public ApplicationManagementViewModel ApplicationManagement { get; }

        /// <summary>
        /// UI Commands ViewModel - handles UI-specific commands like copy/open
        /// </summary>
        public UICommandsViewModel UICommands { get; }

        // Main window properties
        public string StatusMessage => _statusService.CurrentMessage;
        public string ApplicationTitle => _configService.UIConfig.ApplicationTitle;
        public bool IsLoading => ProfileManagement.IsLoading;

        // Expose commonly used properties for easy data binding (delegated to child ViewModels)
        public System.Collections.ObjectModel.ObservableCollection<Models.ProfileInfo> Profiles => ProfileManagement.Profiles;
        public System.Collections.ObjectModel.ObservableCollection<Models.ExternalApplication> ExternalApplications => ApplicationManagement.ExternalApplications;
        
        public Models.ProfileInfo? SelectedProfile 
        { 
            get => ProfileManagement.SelectedProfile; 
            set => ProfileManagement.SelectedProfile = value; 
        }

        public string NewBackupName 
        { 
            get => ProfileManagement.NewBackupName; 
            set => ProfileManagement.NewBackupName = value; 
        }
        
        public string PlayOnlineDirectory 
        { 
            get => ProfileManagement.PlayOnlineDirectory; 
            set => ProfileManagement.PlayOnlineDirectory = value; 
        }
        
        public bool ShowAutoBackups 
        { 
            get => ProfileManagement.ShowAutoBackups; 
            set => ProfileManagement.ShowAutoBackups = value; 
        }
        
        public string ActiveLoginStatus => ProfileManagement.ActiveLoginStatus;

        #endregion

        #region Commands

        // Expose commonly used commands for easy data binding (delegated to child ViewModels)
        
        // Profile Commands
        public System.Windows.Input.ICommand RefreshCommand => ProfileManagement.RefreshCommand;
        public System.Windows.Input.ICommand SwapProfileCommand => ProfileManagement.SwapProfileCommand;
        public System.Windows.Input.ICommand CreateBackupCommand => ProfileManagement.CreateBackupCommand;
        public System.Windows.Input.ICommand DeleteProfileCommand => ProfileManagement.DeleteProfileCommand;
        public System.Windows.Input.ICommand ChangeDirectoryCommand => ProfileManagement.ChangeDirectoryCommand;
        public System.Windows.Input.ICommand RenameProfileCommand => ProfileManagement.RenameProfileCommand;
        public System.Windows.Input.ICommand SwapProfileParameterCommand => ProfileManagement.SwapProfileParameterCommand;
        public System.Windows.Input.ICommand DeleteProfileParameterCommand => ProfileManagement.DeleteProfileParameterCommand;
        public System.Windows.Input.ICommand RenameProfileParameterCommand => ProfileManagement.RenameProfileParameterCommand;

        // Application Commands
        public System.Windows.Input.ICommand LaunchApplicationCommand => ApplicationManagement.LaunchApplicationCommand;
        public System.Windows.Input.ICommand KillApplicationCommand => ApplicationManagement.KillApplicationCommand;
        public System.Windows.Input.ICommand EditApplicationCommand => ApplicationManagement.EditApplicationCommand;
        public System.Windows.Input.ICommand RemoveApplicationCommand => ApplicationManagement.RemoveApplicationCommand;
        public System.Windows.Input.ICommand AddApplicationCommand => ApplicationManagement.AddApplicationCommand;
        public System.Windows.Input.ICommand RefreshApplicationsCommand => ApplicationManagement.RefreshApplicationsCommand;

        // UI Commands
        public System.Windows.Input.ICommand CopyProfileNameParameterCommand => UICommands.CopyProfileNameParameterCommand;
        public System.Windows.Input.ICommand OpenFileLocationParameterCommand => UICommands.OpenFileLocationParameterCommand;

        // Main ViewModel specific commands
        public System.Windows.Input.ICommand ShowAddProfileDialogCommand { get; private set; } = null!;

        private void InitializeCommands()
        {
            ShowAddProfileDialogCommand = new RelayCommand(ShowAddProfileDialog);
        }

        private void ShowAddProfileDialog()
        {
            var dialog = new AddProfileDialog();
            dialog.DataContext = this;
            var result = dialog.ShowDialog();
            // Profile creation is handled by ProfileManagement.CreateBackupCommand
        }

        #endregion

        #region Private Methods

        private async void InitializeAsync()
        {
            // Load data asynchronously
            await ProfileManagement.RefreshProfilesAsync();
            await ApplicationManagement.LoadExternalApplicationsAsync();
        }

        #endregion
    }
}