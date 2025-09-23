using FFXIManager.Configuration;
using FFXIManager.Infrastructure;
using FFXIManager.Services;
using FFXIManager.ViewModels.Base;
using FFXIManager.Views;

namespace FFXIManager.ViewModels
{
    /// <summary>
    /// Simple Main ViewModel that coordinates between specialized ViewModels
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private readonly IStatusMessageService _statusService;
        private readonly IConfigurationService _configService;

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
            INotificationServiceEnhanced notificationServiceEnhanced,
            IExternalApplicationService applicationService,
            IPlayOnlineMonitorService playOnlineMonitorService,
            ICharacterOrderingService characterOrderingService,
            IHotkeyActivationService hotkeyActivationService,
            IUiDispatcher uiDispatcher,
            IHotkeyMappingService hotkeyMappingService,
            IPlayOnlineMemberAccountService memberAccountService,
            IOTPService otpService)
        {
            _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));

            // Create specialized ViewModels with their specific dependencies
            ProfileManagement = new ProfileManagementViewModel(
                profileOperations, statusService, settingsService,
                profileService, dialogService, validationService,
                notificationServiceEnhanced,
                uiDispatcher);

            ApplicationManagement = new ApplicationManagementViewModel(
                applicationService, statusService, loggingService, uiDispatcher);

            PlayOnlineMonitor = new PlayOnlineMonitorViewModel(
                playOnlineMonitorService, statusService, loggingService,
                characterOrderingService, hotkeyActivationService,
                settingsService, hotkeyMappingService);

            PlayOnlineMemberAccounts = new PlayOnlineMemberAccountsViewModel(
                memberAccountService, statusService, loggingService,
                dialogService, uiDispatcher, otpService);

            UICommands = new UICommandsViewModel(uiCommandService, notificationServiceEnhanced);

            // Subscribe to status message changes
            _statusService.MessageChanged += (_, message) => OnPropertyChanged(nameof(StatusMessage));

            // Subscribe to property changes from ProfileManagement
            ProfileManagement.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ProfileManagement.IsLoading))
                {
                    OnPropertyChanged(nameof(IsLoading));
                }
                else if (e.PropertyName == nameof(ProfileManagement.PlayOnlineDirectory))
                {
                    OnPropertyChanged(nameof(PlayOnlineDirectory));
                }
                else if (e.PropertyName == nameof(ProfileManagement.ShowAutoBackups))
                {
                    OnPropertyChanged(nameof(ShowAutoBackups));
                }
                else if (e.PropertyName == nameof(ProfileManagement.NewBackupName))
                {
                    OnPropertyChanged(nameof(NewBackupName));
                }
                else if (e.PropertyName == nameof(ProfileManagement.SelectedProfile))
                {
                    OnPropertyChanged(nameof(SelectedProfile));
                    // Notify command can execute changed for commands that depend on SelectedProfile
                    ((RelayCommand)CopyProfileNameCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)OpenFileLocationCommand).RaiseCanExecuteChanged();
                }
                else if (e.PropertyName == nameof(ProfileManagement.ActiveLoginStatus))
                {
                    OnPropertyChanged(nameof(ActiveLoginStatus));
                }
            };

            // Update PlayOnlineMemberAccounts when profile selection changes
            ProfileManagement.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ProfileManagement.SelectedProfile))
                {
                    PlayOnlineMemberAccounts.CurrentProfile = ProfileManagement.SelectedProfile;
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
        /// Play Online Character Monitor ViewModel - handles character detection and window switching
        /// </summary>
        public PlayOnlineMonitorViewModel PlayOnlineMonitor { get; }

        /// <summary>
        /// PlayOnline Member Accounts ViewModel - handles POL account associations
        /// </summary>
        public PlayOnlineMemberAccountsViewModel PlayOnlineMemberAccounts { get; }

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
        public System.Windows.Input.ICommand CopyProfileNameCommand { get; private set; } = null!;
        public System.Windows.Input.ICommand OpenFileLocationCommand { get; private set; } = null!;

        // Main ViewModel specific commands
        public System.Windows.Input.ICommand ShowAddProfileDialogCommand { get; private set; } = null!;

        private void InitializeCommands()
        {
            ShowAddProfileDialogCommand = new RelayCommand(ShowAddProfileDialog);
            CopyProfileNameCommand = new RelayCommand(CopySelectedProfileName, () => SelectedProfile != null);
            OpenFileLocationCommand = new RelayCommand(OpenSelectedFileLocation, () => SelectedProfile != null);
        }

        private void ShowAddProfileDialog()
        {
            var dialog = new AddProfileDialog();
            dialog.DataContext = this;
            var result = dialog.ShowDialog();
            // Profile creation is handled by ProfileManagement.CreateBackupCommand
        }

        private void CopySelectedProfileName()
        {
            if (SelectedProfile != null)
            {
                UICommands.CopyProfileNameParameterCommand.Execute(SelectedProfile);
            }
        }

        private void OpenSelectedFileLocation()
        {
            if (SelectedProfile != null)
            {
                UICommands.OpenFileLocationParameterCommand.Execute(SelectedProfile);
            }
        }

        #endregion

        #region Private Methods

        private async void InitializeAsync()
        {
            try
            {
                // Load data asynchronously without blocking the UI
                await ProfileManagement.RefreshProfilesAsync();
                await ApplicationManagement.LoadExternalApplicationsAsync();

                // Load character data using the public method
                await PlayOnlineMonitor.LoadCharactersAsync();
            }
            catch (Exception ex)
            {
                _statusService.SetMessage($"Error during initialization: {ex.Message}");
            }
        }

        #endregion
    }
}

