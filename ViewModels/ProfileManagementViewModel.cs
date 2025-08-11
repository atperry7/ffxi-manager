using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.ViewModels.Base;
using System.Collections.ObjectModel;
using System.Windows.Input;
using FFXIManager.Infrastructure;

namespace FFXIManager.ViewModels
{
    using System.Text.RegularExpressions;
    /// <summary>
    /// ViewModel responsible for Profile Management operations
    /// </summary>
    public partial class ProfileManagementViewModel : ViewModelBase
    {
        private readonly IProfileOperationsService _profileOperations;
        private readonly IStatusMessageService _statusService;
        private readonly ISettingsService _settingsService;
        private readonly IProfileService _profileService;
        private readonly IDialogService _dialogService;
        private readonly IValidationService _validationService;
        
        private ApplicationSettings _settings;
        private ProfileInfo? _selectedProfile;
        private bool _isLoading;
        private string _newBackupName = string.Empty;

        public ProfileManagementViewModel(
            IProfileOperationsService profileOperations,
            IStatusMessageService statusService,
            ISettingsService settingsService,
            IProfileService profileService,
            IDialogService dialogService,
            IValidationService validationService)
        {
            _profileOperations = profileOperations ?? throw new ArgumentNullException(nameof(profileOperations));
            _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));

            _settings = _settingsService.LoadSettings();
            _profileService.PlayOnlineDirectory = _settings.PlayOnlineDirectory;

            Profiles = new ObservableCollection<ProfileInfo>();
            InitializeCommands();
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

        public ProfileInfo? ActiveLoginInfo { get; private set; }
        
        public string ActiveLoginStatus
        {
            get
            {
                if (ActiveLoginInfo == null)
                    return "? No active login file found";
                
                var displayText = ActiveLoginInfo.Name;
                if (displayText.Contains("(Last Set:"))
                {
                    var match = LastSetRegex().Match(displayText);
                    if (match.Success)
                    {
                        var lastSetProfile = match.Groups[1].Value;
                        return $"Currently Active: '{lastSetProfile}' profile ({ActiveLoginInfo.FileSizeFormatted}) - Modified: {ActiveLoginInfo.LastModified:yyyy-MM-dd HH:mm}";
                    }
                }
                
                return $"Current: System file ({ActiveLoginInfo.FileSizeFormatted}) - Modified: {ActiveLoginInfo.LastModified:yyyy-MM-dd HH:mm}";
            }
        }

        public string? CurrentActiveProfileName
        {
            get
            {
                if (ActiveLoginInfo?.Name?.Contains("(Last Set:") == true)
                {
                    var match = LastSetRegex().Match(ActiveLoginInfo.Name);
                    return match.Success ? match.Groups[1].Value : null;
                }
                return null;
            }
        }

        #endregion

        [GeneratedRegex("Last Set: ([^)]+)")]
        private static partial Regex LastSetRegex();

        #region Commands

        public ICommand RefreshCommand { get; private set; } = null!;
        public ICommand SwapProfileCommand { get; private set; } = null!;
        public ICommand CreateBackupCommand { get; private set; } = null!;
        public ICommand DeleteProfileCommand { get; private set; } = null!;
        public ICommand ChangeDirectoryCommand { get; private set; } = null!;
        public ICommand RenameProfileCommand { get; private set; } = null!;
        public ICommand SwapProfileParameterCommand { get; private set; } = null!;
        public ICommand DeleteProfileParameterCommand { get; private set; } = null!;
        public ICommand RenameProfileParameterCommand { get; private set; } = null!;

        private void InitializeCommands()
        {
            RefreshCommand = new RelayCommand(async () => await RefreshProfilesAsync());
            SwapProfileCommand = new RelayCommand(async () => await SwapProfileAsync(), CanSwapProfile);
            CreateBackupCommand = new RelayCommand(async () => await CreateBackupAsync(), CanCreateBackup);
            DeleteProfileCommand = new RelayCommand(async () => await DeleteProfileAsync(), CanDeleteProfile);
            ChangeDirectoryCommand = new RelayCommand(ChangeDirectory);
            RenameProfileCommand = new RelayCommand(async () => await RenameProfileAsync(), CanRenameProfile);

            SwapProfileParameterCommand = new RelayCommandWithParameter<ProfileInfo>(
                async profile => await SwapProfileAsync(profile), 
                profile => profile != null && !profile.IsSystemFile);
            DeleteProfileParameterCommand = new RelayCommandWithParameter<ProfileInfo>(
                async profile => await DeleteProfileAsync(profile), 
                profile => profile != null && !profile.IsSystemFile && !profile.IsCurrentlyActive);
            RenameProfileParameterCommand = new RelayCommandWithParameter<ProfileInfo>(
                async profile => await RenameProfileParameterAsync(profile), 
                profile => profile != null && !profile.IsSystemFile);
        }

        #endregion

        #region Public Methods

        public async Task RefreshProfilesAsync()
        {
            try
            {
                IsLoading = true;
                _statusService.SetMessage("Loading profiles...");

                var profiles = await _profileOperations.LoadProfilesAsync(_settings.ShowAutoBackupsInList);
                var activeLoginInfo = await _profileOperations.GetActiveLoginInfoAsync();

                await ServiceLocator.UiDispatcher.InvokeAsync(() => UpdateProfilesCollection(profiles, activeLoginInfo));

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

        #endregion

        #region Private Methods

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
                    _settings.LastUsedProfile = profile.Name;
                    _settings.LastActiveProfileName = profile.Name;
                    _settingsService.SaveSettings(_settings);
                    
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
                var validation = _validationService.ValidateProfileName(NewBackupName);
                if (!validation.IsValid)
                {
                    _statusService.SetMessage($"{validation.ErrorMessage}");
                    return;
                }

                IsLoading = true;
                _statusService.SetMessage($"Creating backup: {NewBackupName}");

                var (success, message, newProfile) = await _profileOperations.CreateBackupAsync(NewBackupName);
                _statusService.SetMessage(message);

                if (success && newProfile != null)
                {
                    await ServiceLocator.UiDispatcher.InvokeAsync(() => Profiles.Add(newProfile));
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
                    await ServiceLocator.UiDispatcher.InvokeAsync(() =>
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
            if (SelectedProfile == null || SelectedProfile.IsSystemFile) return;

            try
            {
                var oldName = SelectedProfile.Name;
                var newName = await _dialogService.ShowRenameDialogAsync(SelectedProfile.Name, SelectedProfile.IsSystemFile);
                if (newName == null) return;
                
                var validation = _validationService.ValidateProfileName(newName);
                if (!validation.IsValid)
                {
                    _statusService.SetMessage($"{validation.ErrorMessage}");
                    return;
                }

                IsLoading = true;
                _statusService.SetMessage($"Renaming profile '{SelectedProfile.Name}' to '{newName}'...");

                var (success, message) = await _profileOperations.RenameProfileAsync(SelectedProfile, newName);
                _statusService.SetMessage(message);

                if (success)
                {
                    if (_settings.LastUsedProfile == oldName)
                    {
                        _settings.LastUsedProfile = newName;
                        _settingsService.SaveSettings(_settings);
                    }
                    
                    await RefreshProfilesAsync();
                    
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

        private async void ChangeDirectory()
        {
            try
            {
                var selectedPath = await _dialogService.ShowFolderBrowserDialogAsync("Select PlayOnline Directory", PlayOnlineDirectory);
                if (selectedPath != null)
                {
                    var validation = _validationService.ValidateDirectory(selectedPath);
                    if (!validation.IsValid)
                    {
                        _statusService.SetMessage($"{validation.ErrorMessage}");
                        return;
                    }

                    PlayOnlineDirectory = selectedPath;
                }
            }
            catch (Exception ex)
            {
                _statusService.SetMessage($"Error selecting directory: {ex.Message}");
            }
        }

        private async Task RenameProfileParameterAsync(ProfileInfo profile)
        {
            if (profile?.IsSystemFile != false) return;

            var originalSelection = SelectedProfile;
            SelectedProfile = profile;
            
            try
            {
                await RenameProfileAsync();
            }
            finally
            {
                if (SelectedProfile == profile && originalSelection != profile)
                {
                    SelectedProfile = originalSelection;
                }
            }
        }

        private void UpdateProfilesCollection(List<ProfileInfo> profiles, ProfileInfo? activeLoginInfo)
        {
            Profiles.Clear();
            ProfileInfo? lastUserChoice = null;
            ProfileInfo? lastUsedProfile = null;
            ProfileInfo? currentActiveProfile = null;

            ActiveLoginInfo = activeLoginInfo;
            OnPropertyChanged(nameof(ActiveLoginInfo));
            OnPropertyChanged(nameof(ActiveLoginStatus));
            OnPropertyChanged(nameof(CurrentActiveProfileName));

            var activeProfileName = CurrentActiveProfileName;
            bool activeProfileExists = !string.IsNullOrEmpty(activeProfileName) && 
                profiles.Any(p => p.Name.Equals(activeProfileName, StringComparison.OrdinalIgnoreCase));

            foreach (var profile in profiles)
            {
                profile.IsCurrentlyActive = !string.IsNullOrEmpty(activeProfileName) && 
                                          profile.Name.Equals(activeProfileName, StringComparison.OrdinalIgnoreCase) &&
                                          activeProfileExists;
                                
                Profiles.Add(profile);

                if (profile.IsLastUserChoice) lastUserChoice = profile;
                if (profile.Name == _settings.LastUsedProfile && !profile.IsSystemFile) lastUsedProfile = profile;
                if (profile.IsCurrentlyActive) currentActiveProfile = profile;
            }

            // Selection priority: 1. Currently active, 2. Last used, 3. Last choice, 4. Most recent
            if (currentActiveProfile != null)
            {
                SelectedProfile = currentActiveProfile;
                if (_settings.LastUsedProfile != currentActiveProfile.Name)
                {
                    _settings.LastUsedProfile = currentActiveProfile.Name;
                    _settingsService.SaveSettings(_settings);
                }
            }
            else if (lastUsedProfile != null && profiles.Contains(lastUsedProfile))
            {
                SelectedProfile = lastUsedProfile;
            }
            else if (lastUserChoice != null && profiles.Contains(lastUserChoice))
            {
                SelectedProfile = lastUserChoice;
            }
            else
            {
                var fallbackProfile = profiles
                    .Where(p => !p.Name.StartsWith("backup_", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(p => p.LastModified)
                    .FirstOrDefault();
                
                SelectedProfile = fallbackProfile ?? profiles.FirstOrDefault();
            }
        }

        private void UpdateCommandStates()
        {
            ((RelayCommand)SwapProfileCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DeleteProfileCommand).RaiseCanExecuteChanged();
            ((RelayCommand)RenameProfileCommand).RaiseCanExecuteChanged();
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
        private bool CanDeleteProfile() => SelectedProfile != null && !SelectedProfile.IsSystemFile && !SelectedProfile.IsCurrentlyActive;
        private bool CanRenameProfile() => SelectedProfile != null && !SelectedProfile.IsSystemFile;
        private bool CanCreateBackup() => !string.IsNullOrWhiteSpace(NewBackupName);

        #endregion
    }
}