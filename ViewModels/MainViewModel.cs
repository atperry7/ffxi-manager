using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using FFXIManager.Models;
using FFXIManager.Services;

namespace FFXIManager.ViewModels
{
    /// <summary>
    /// ViewModel for the main window
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ProfileService _profileService;
        private readonly SettingsService _settingsService;
        private ApplicationSettings _settings;
        private ProfileInfo? _selectedProfile;
        private bool _isLoading;
        private string _statusMessage = string.Empty;
        private string _newBackupName = string.Empty;
        
        public MainViewModel()
        {
            _settingsService = new SettingsService();
            _settings = _settingsService.LoadSettings();
            
            _profileService = new ProfileService
            {
                PlayOnlineDirectory = _settings.PlayOnlineDirectory
            };
            
            Profiles = new ObservableCollection<ProfileInfo>();
            
            // Initialize commands
            RefreshCommand = new RelayCommand(async () => await RefreshProfilesAsync());
            SwapProfileCommand = new RelayCommand(async () => await SwapProfileAsync(), () => SelectedProfile != null && !SelectedProfile.IsActive);
            CreateBackupCommand = new RelayCommand(async () => await CreateBackupAsync(), () => !string.IsNullOrWhiteSpace(NewBackupName));
            DeleteProfileCommand = new RelayCommand(async () => await DeleteProfileAsync(), () => SelectedProfile != null && !SelectedProfile.IsActive);
            ChangeDirectoryCommand = new RelayCommand(ChangeDirectory);
            CleanupAutoBackupsCommand = new RelayCommand(async () => await CleanupAutoBackupsAsync());
            
            // Load profiles on startup if auto-refresh is enabled
            if (_settings.AutoRefreshOnStartup)
            {
                _ = Task.Run(async () => await RefreshProfilesAsync());
            }
        }
        
        public ObservableCollection<ProfileInfo> Profiles { get; }
        
        public ProfileInfo? SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (SetProperty(ref _selectedProfile, value))
                {
                    ((RelayCommand)SwapProfileCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)DeleteProfileCommand).RaiseCanExecuteChanged();
                    
                    // Save last used profile
                    if (value != null)
                    {
                        _settings.LastUsedProfile = value.Name;
                        _settingsService.SaveSettings(_settings);
                    }
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
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
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
        
        public ICommand RefreshCommand { get; }
        public ICommand SwapProfileCommand { get; }
        public ICommand CreateBackupCommand { get; }
        public ICommand DeleteProfileCommand { get; }
        public ICommand ChangeDirectoryCommand { get; }
        public ICommand CleanupAutoBackupsCommand { get; }
        
        private async Task RefreshProfilesAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Loading profiles...";
                
                // Get profiles based on user preferences
                var profiles = _settings.ShowAutoBackupsInList 
                    ? await _profileService.GetProfilesAsync() 
                    : await _profileService.GetUserProfilesAsync();
                    
                var activeLoginInfo = await _profileService.GetActiveLoginInfoAsync();
                
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Profiles.Clear();
                    ProfileInfo? lastUsedProfile = null;
                    
                    // Add active login info for display purposes (but not selectable for swapping)
                    if (activeLoginInfo != null)
                    {
                        Profiles.Add(activeLoginInfo);
                    }
                    
                    // Add backup profiles
                    foreach (var profile in profiles)
                    {
                        Profiles.Add(profile);
                        
                        // Try to restore last selected profile (only for backup profiles)
                        if (profile.Name == _settings.LastUsedProfile)
                        {
                            lastUsedProfile = profile;
                        }
                    }
                    
                    // Restore last selected profile if found and it's not the active login file
                    if (lastUsedProfile != null && !lastUsedProfile.IsActive)
                    {
                        SelectedProfile = lastUsedProfile;
                    }
                });
                
                var autoBackupCount = _settings.ShowAutoBackupsInList ? 0 : (await _profileService.GetAutoBackupsAsync()).Count;
                var statusSuffix = _settings.ShowAutoBackupsInList ? "" : $" ({autoBackupCount} auto-backups hidden)";
                StatusMessage = $"Loaded {profiles.Count} backup profiles{statusSuffix}";
                
                if (activeLoginInfo == null)
                {
                    StatusMessage += " (Warning: No active login_w.bin file found)";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading profiles: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }
        
        private async Task SwapProfileAsync()
        {
            if (SelectedProfile == null) return;
            
            try
            {
                IsLoading = true;
                StatusMessage = $"Swapping to profile: {SelectedProfile.Name}";
                
                await _profileService.SwapProfileAsync(SelectedProfile);
                
                StatusMessage = $"Successfully swapped to profile: {SelectedProfile.Name}";
                await RefreshProfilesAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error swapping profile: {ex.Message}";
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
                IsLoading = true;
                StatusMessage = $"Creating backup: {NewBackupName}";
                
                var newProfile = await _profileService.CreateBackupAsync(NewBackupName);
                
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Profiles.Add(newProfile);
                });
                
                NewBackupName = string.Empty;
                StatusMessage = $"Successfully created backup: {newProfile.Name}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error creating backup: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }
        
        private async Task DeleteProfileAsync()
        {
            if (SelectedProfile == null) return;
            
            MessageBoxResult result = MessageBoxResult.Yes;
            
            if (_settings.ConfirmDeleteOperations)
            {
                result = MessageBox.Show(
                    $"Are you sure you want to delete profile '{SelectedProfile.Name}'?",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
            }
            
            if (result != MessageBoxResult.Yes) return;
            
            try
            {
                IsLoading = true;
                StatusMessage = $"Deleting profile: {SelectedProfile.Name}";
                
                await _profileService.DeleteProfileAsync(SelectedProfile);
                
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Profiles.Remove(SelectedProfile);
                });
                
                SelectedProfile = null;
                StatusMessage = "Profile deleted successfully";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error deleting profile: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }
        
        private void ChangeDirectory()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select PlayOnline Directory",
                InitialDirectory = PlayOnlineDirectory
            };
            
            if (dialog.ShowDialog() == true)
            {
                PlayOnlineDirectory = dialog.FolderName;
            }
        }
        
        private async Task CleanupAutoBackupsAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Cleaning up auto-backups...";
                
                var autoBackups = await _profileService.GetAutoBackupsAsync();
                var countBefore = autoBackups.Count;
                
                await _profileService.CleanupAutoBackupsAsync();
                
                var autoBackupsAfter = await _profileService.GetAutoBackupsAsync();
                var countAfter = autoBackupsAfter.Count;
                var deletedCount = countBefore - countAfter;
                
                StatusMessage = $"Cleaned up {deletedCount} old auto-backup files";
                await RefreshProfilesAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error cleaning up auto-backups: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }
        
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
    }
}