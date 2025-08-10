using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.Views;
using FFXIManager.Infrastructure;
using FFXIManager.Configuration;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace FFXIManager.ViewModels
{
    /// <summary>
    /// SIMPLIFIED ViewModel for the main window - now focused only on UI state management
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly IProfileOperationsService _profileOperations;
        private readonly IStatusMessageService _statusService;
        private readonly ISettingsService _settingsService;
        private readonly IProfileService _profileService;
        private readonly IUICommandService _uiCommandService;
        private readonly IDialogService _dialogService;
        private readonly IConfigurationService _configService;
        private readonly IValidationService _validationService;
        private readonly ILoggingService _loggingService;
        private readonly INotificationService _notificationService;
        
        private ApplicationSettings _settings;
        private ProfileInfo? _selectedProfile;
        private bool _isLoading;
        private string _newBackupName = string.Empty;

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
            ServiceLocator.NotificationService)
        {
        }

        // Constructor for dependency injection (testability)
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
            INotificationService notificationService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _profileOperations = profileOperations ?? throw new ArgumentNullException(nameof(profileOperations));
            _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
            _uiCommandService = uiCommandService ?? throw new ArgumentNullException(nameof(uiCommandService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));

            _settings = _settingsService.LoadSettings();
            _profileService.PlayOnlineDirectory = _settings.PlayOnlineDirectory;

            Profiles = new ObservableCollection<ProfileInfo>();
            
            // Subscribe to status message changes
            _statusService.MessageChanged += (_, message) => OnPropertyChanged(nameof(StatusMessage));

            InitializeCommands();
            
            // Load profiles on startup if auto-refresh is enabled
            if (_settings.AutoRefreshOnStartup)
            {
                _ = Task.Run(async () => await RefreshProfilesAsync());
            }
        }

        #region Properties

        public ObservableCollection<ProfileInfo> Profiles { get; }

        public ProfileInfo? SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (SetProperty(ref _selectedProfile, value))
                {
                    UpdateCommandStates();
                    SaveLastUsedProfile(value);
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string StatusMessage
        {
            get => _statusService.CurrentMessage;
            private set { } // Remove the setter that causes the loop
        }

        public string NewBackupName
        {
            get => _newBackupName;
            set
            {
                if (SetProperty(ref _newBackupName, value))
                {
                    ((RelayCommand)CreateBackupCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public string PlayOnlineDirectory
        {
            get => _profileService.PlayOnlineDirectory;
            set
            {
                if (_profileService.PlayOnlineDirectory != value)
                {
                    _profileService.PlayOnlineDirectory = value;
                    _settings.PlayOnlineDirectory = value;
                    _settingsService.SaveSettings(_settings);
                    OnPropertyChanged();
                    _ = Task.Run(async () => await RefreshProfilesAsync());
                }
            }
        }

        public bool ShowAutoBackups
        {
            get => _settings.ShowAutoBackupsInList;
            set
            {
                if (_settings.ShowAutoBackupsInList != value)
                {
                    _settings.ShowAutoBackupsInList = value;
                    _settingsService.SaveSettings(_settings);
                    OnPropertyChanged();
                    _ = Task.Run(async () => await RefreshProfilesAsync());
                }
            }
        }

        public string ApplicationTitle => _configService.UIConfig.ApplicationTitle;

        public ProfileInfo? ActiveLoginInfo { get; private set; }
        
        public string ActiveLoginStatus
        {
            get
            {
                if (ActiveLoginInfo == null)
                    return "?? No active login file found";
                
                return $"?? Current: {ActiveLoginInfo.Name} ({ActiveLoginInfo.FileSizeFormatted}) - Modified: {ActiveLoginInfo.LastModified:yyyy-MM-dd HH:mm}";
            }
        }

        #endregion

        #region Commands

        public ICommand RefreshCommand { get; private set; } = null!;
        public ICommand SwapProfileCommand { get; private set; } = null!;
        public ICommand CreateBackupCommand { get; private set; } = null!;
        public ICommand DeleteProfileCommand { get; private set; } = null!;
        public ICommand ChangeDirectoryCommand { get; private set; } = null!;
        public ICommand CleanupAutoBackupsCommand { get; private set; } = null!;
        public ICommand ResetTrackingCommand { get; private set; } = null!;
        public ICommand SwapProfileParameterCommand { get; private set; } = null!;
        public ICommand DeleteProfileParameterCommand { get; private set; } = null!;
        public ICommand RenameProfileCommand { get; private set; } = null!;
        public ICommand CopyProfileNameCommand { get; private set; } = null!;
        public ICommand OpenFileLocationCommand { get; private set; } = null!;
        public ICommand RenameProfileParameterCommand { get; private set; } = null!;
        public ICommand CopyProfileNameParameterCommand { get; private set; } = null!;
        public ICommand OpenFileLocationParameterCommand { get; private set; } = null!;

        private void InitializeCommands()
        {
            RefreshCommand = new RelayCommand(async () => await RefreshProfilesAsync());
            SwapProfileCommand = new RelayCommand(async () => await SwapProfileAsync(), CanSwapProfile);
            CreateBackupCommand = new RelayCommand(async () => await CreateBackupAsync(), CanCreateBackup);
            DeleteProfileCommand = new RelayCommand(async () => await DeleteProfileAsync(), CanDeleteProfile);
            ChangeDirectoryCommand = new RelayCommand(ChangeDirectory);
            CleanupAutoBackupsCommand = new RelayCommand(async () => await CleanupAutoBackupsAsync());
            ResetTrackingCommand = new RelayCommand(async () => await ResetTrackingAsync());
            RenameProfileCommand = new RelayCommand(async () => await RenameProfileAsync(), CanRenameProfile);

            // Parameterized commands
            SwapProfileParameterCommand = new RelayCommandWithParameter<ProfileInfo>(
                async profile => await SwapProfileAsync(profile), 
                profile => profile != null && !profile.IsSystemFile);
            DeleteProfileParameterCommand = new RelayCommandWithParameter<ProfileInfo>(
                async profile => await DeleteProfileAsync(profile), 
                profile => profile != null && !profile.IsSystemFile);
            RenameProfileParameterCommand = new RelayCommandWithParameter<ProfileInfo>(
                async profile => await RenameProfileParameterAsync(profile), 
                profile => profile != null && !profile.IsSystemFile);
            
            // UI Commands
            CopyProfileNameCommand = new RelayCommand(CopyProfileName, () => SelectedProfile != null);
            OpenFileLocationCommand = new RelayCommand(OpenFileLocation, () => SelectedProfile != null);
            CopyProfileNameParameterCommand = new RelayCommandWithParameter<ProfileInfo>(
                profile => CopyProfileNameParameter(profile), 
                profile => profile != null);
            OpenFileLocationParameterCommand = new RelayCommandWithParameter<ProfileInfo>(
                profile => OpenFileLocationParameter(profile), 
                profile => profile != null);
        }

        #endregion

        #region Command Operations

        private async Task RefreshProfilesAsync()
        {
            try
            {
                IsLoading = true;
                _statusService.SetMessage("Loading profiles...");

                var profiles = await _profileOperations.LoadProfilesAsync(_settings.ShowAutoBackupsInList);
                var activeLoginInfo = await _profileOperations.GetActiveLoginInfoAsync();

                await Application.Current.Dispatcher.InvokeAsync(() => UpdateProfilesCollection(profiles, activeLoginInfo));

                var autoBackupCount = _settings.ShowAutoBackupsInList ? 0 : 
                    (await _profileService.GetAutoBackupsAsync()).Count;
                var statusSuffix = _settings.ShowAutoBackupsInList ? "" : $" ({autoBackupCount} auto-backups hidden)";
                _statusService.SetMessage($"Loaded {profiles.Count} backup profiles{statusSuffix}");

                if (activeLoginInfo == null)
                {
                    _statusService.SetMessage(_statusService.CurrentMessage + " (Warning: No active login_w.bin file found)");
                }
            }
            catch (Exception ex)
            {
                _statusService.SetMessage($"Error loading profiles: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task SwapProfileAsync()
        {
            if (SelectedProfile != null)
                await SwapProfileAsync(SelectedProfile);
        }

        private async Task SwapProfileAsync(ProfileInfo profile)
        {
            if (profile == null) return;

            try
            {
                IsLoading = true;
                _statusService.SetMessage($"Swapping to profile: {profile.Name}");

                var (success, message) = await _profileOperations.SwapProfileAsync(profile);
                _statusService.SetMessage(message);

                if (success)
                {
                    await RefreshProfilesAsync();
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task CreateBackupAsync()
        {
            try
            {
                // Validate backup name first
                var validation = _validationService.ValidateProfileName(NewBackupName);
                if (!validation.IsValid)
                {
                    _statusService.SetMessage($"? {validation.ErrorMessage}");
                    return;
                }

                IsLoading = true;
                _statusService.SetMessage(_configService.UIConfig.StatusMessages.GetValueOrDefault(
                    "CreatingBackup", $"Creating backup: {NewBackupName}"));

                var (success, message, newProfile) = await _profileOperations.CreateBackupAsync(NewBackupName);
                _statusService.SetMessage(message);

                if (success && newProfile != null)
                {
                    Application.Current.Dispatcher.Invoke(() => Profiles.Add(newProfile));
                    NewBackupName = string.Empty;
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task DeleteProfileAsync()
        {
            if (SelectedProfile != null)
                await DeleteProfileAsync(SelectedProfile);
        }

        private async Task DeleteProfileAsync(ProfileInfo profile)
        {
            if (profile == null) return;

            try
            {
                IsLoading = true;
                _statusService.SetMessage($"Deleting profile: {profile.Name}");

                var (success, message) = await _profileOperations.DeleteProfileAsync(profile);
                _statusService.SetMessage(message);

                if (success)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Profiles.Remove(profile);
                        if (SelectedProfile == profile)
                            SelectedProfile = null;
                    });
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task RenameProfileAsync()
        {
            if (SelectedProfile == null) return;

            if (SelectedProfile.IsSystemFile)
            {
                _statusService.SetMessage(_configService.UIConfig.StatusMessages.GetValueOrDefault(
                    "SystemFileRenameError", "Cannot rename the system login file."));
                return;
            }

            try
            {
                var newName = await _dialogService.ShowRenameDialogAsync(SelectedProfile.Name, SelectedProfile.IsSystemFile);
                if (newName == null) return; // User cancelled
                
                // Validate new name
                var validation = _validationService.ValidateProfileName(newName);
                if (!validation.IsValid)
                {
                    _statusService.SetMessage($"? {validation.ErrorMessage}");
                    return;
                }

                IsLoading = true;
                _statusService.SetMessage($"Renaming profile '{SelectedProfile.Name}' to '{newName}'...");

                var (success, message) = await _profileOperations.RenameProfileAsync(SelectedProfile, newName);
                _statusService.SetMessage(message);

                if (success)
                {
                    await RefreshProfilesAsync();
                    
                    // Try to reselect the renamed profile
                    var renamedProfile = Profiles.FirstOrDefault(p => p.Name == newName);
                    if (renamedProfile != null)
                        SelectedProfile = renamedProfile;
                }
            }
            catch (Exception ex)
            {
                _statusService.SetMessage($"Error renaming profile: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task CleanupAutoBackupsAsync()
        {
            try
            {
                IsLoading = true;
                _statusService.SetMessage("Cleaning up auto-backups...");
                
                await Task.Delay(300); // Brief delay for user feedback

                var (success, message, deletedCount) = await _profileOperations.CleanupAutoBackupsAsync();
                
                _statusService.SetMessage("Refreshing profiles after cleanup...");
                await RefreshProfilesAsync();

                if (success)
                {
                    _statusService.SetTemporaryMessage($"Cleaned up {deletedCount} old auto-backup files", TimeSpan.FromSeconds(3));
                }
                else
                {
                    _statusService.SetMessage(message);
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ResetTrackingAsync()
        {
            try
            {
                IsLoading = true;
                _statusService.SetMessage("Resetting user profile choice...");
                
                await Task.Delay(300); // Brief delay for user feedback

                var (success, message) = await _profileOperations.ResetTrackingAsync();
                
                _statusService.SetMessage("Refreshing profiles after reset...");
                await RefreshProfilesAsync();

                if (success)
                {
                    _statusService.SetTemporaryMessage(message, TimeSpan.FromSeconds(3));
                }
                else
                {
                    _statusService.SetMessage(message);
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async void ChangeDirectory()
        {
            try
            {
                var selectedPath = await _dialogService.ShowFolderBrowserDialogAsync("Select PlayOnline Directory", PlayOnlineDirectory);
                if (selectedPath != null)
                {
                    // Validate directory
                    var validation = _validationService.ValidateDirectory(selectedPath);
                    if (!validation.IsValid)
                    {
                        _statusService.SetMessage($"? {validation.ErrorMessage}");
                        return;
                    }

                    PlayOnlineDirectory = selectedPath;
                }
            }
            catch (Exception ex)
            {
                _statusService.SetMessage($"? Error selecting directory: {ex.Message}");
            }
        }

        private void CopyProfileName()
        {
            if (SelectedProfile == null) return;
            
            try
            {
                _uiCommandService.CopyToClipboard(SelectedProfile.Name);
                _statusService.SetMessage($"?? Copied profile name: {SelectedProfile.Name}");
            }
            catch (Exception ex)
            {
                _statusService.SetMessage($"? Error copying to clipboard: {ex.Message}");
            }
        }

        private void OpenFileLocation()
        {
            if (SelectedProfile == null) return;
            
            try
            {
                _uiCommandService.OpenFileLocation(SelectedProfile.FilePath);
                _statusService.SetMessage($"?? Opened file location for: {SelectedProfile.Name}");
            }
            catch (Exception ex)
            {
                _statusService.SetMessage($"? {ex.Message}");
            }
        }

        private async Task RenameProfileParameterAsync(ProfileInfo profile)
        {
            if (profile == null) return;

            if (profile.IsSystemFile)
            {
                _statusService.SetMessage("Cannot rename the system login file (login_w.bin).");
                return;
            }

            // Temporarily select this profile for the operation
            var originalSelection = SelectedProfile;
            SelectedProfile = profile;
            
            try
            {
                await RenameProfileAsync();
            }
            finally
            {
                // Restore original selection if rename didn't change it
                if (SelectedProfile == profile && originalSelection != profile)
                {
                    SelectedProfile = originalSelection;
                }
            }
        }

        private void CopyProfileNameParameter(ProfileInfo profile)
        {
            if (profile == null) return;
            
            try
            {
                _uiCommandService.CopyToClipboard(profile.Name);
                _statusService.SetMessage($"?? Copied profile name: {profile.Name}");
            }
            catch (Exception ex)
            {
                _statusService.SetMessage($"? Error copying to clipboard: {ex.Message}");
            }
        }

        private void OpenFileLocationParameter(ProfileInfo profile)
        {
            if (profile == null) return;
            
            try
            {
                _uiCommandService.OpenFileLocation(profile.FilePath);
                _statusService.SetMessage($"?? Opened file location for: {profile.Name}");
            }
            catch (Exception ex)
            {
                _statusService.SetMessage($"? {ex.Message}");
            }
        }

        #endregion

        #region Helper Methods

        private void UpdateProfilesCollection(List<ProfileInfo> profiles, ProfileInfo? activeLoginInfo)
        {
            Profiles.Clear();
            ProfileInfo? lastUserChoice = null;
            ProfileInfo? lastUsedProfile = null;

            // Store active login info separately (don't add to profiles list)
            ActiveLoginInfo = activeLoginInfo;
            OnPropertyChanged(nameof(ActiveLoginInfo));
            OnPropertyChanged(nameof(ActiveLoginStatus));

            // Add only backup profiles (exclude system file)
            foreach (var profile in profiles)
            {
                Profiles.Add(profile);

                if (profile.IsLastUserChoice)
                    lastUserChoice = profile;
                
                if (profile.Name == _settings.LastUsedProfile && !profile.IsSystemFile)
                    lastUsedProfile = profile;
            }

            // Restore selection preference
            if (lastUserChoice != null && profiles.Contains(lastUserChoice))
                SelectedProfile = lastUserChoice;
            else if (lastUsedProfile != null && profiles.Contains(lastUsedProfile))
                SelectedProfile = lastUsedProfile;
            else
                SelectedProfile = null;
        }

        private void UpdateCommandStates()
        {
            ((RelayCommand)SwapProfileCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DeleteProfileCommand).RaiseCanExecuteChanged();
            ((RelayCommand)RenameProfileCommand).RaiseCanExecuteChanged();
            ((RelayCommand)CopyProfileNameCommand).RaiseCanExecuteChanged();
            ((RelayCommand)OpenFileLocationCommand).RaiseCanExecuteChanged();
        }

        private void SaveLastUsedProfile(ProfileInfo? profile)
        {
            if (profile != null)
            {
                _settings.LastUsedProfile = profile.Name;
                _settingsService.SaveSettings(_settings);
            }
        }

        private bool CanSwapProfile() => SelectedProfile != null && !SelectedProfile.IsSystemFile;
        private bool CanDeleteProfile() => SelectedProfile != null && !SelectedProfile.IsSystemFile;
        private bool CanRenameProfile() => SelectedProfile != null && !SelectedProfile.IsSystemFile;
        private bool CanCreateBackup() => !string.IsNullOrWhiteSpace(NewBackupName);

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion
    }
}