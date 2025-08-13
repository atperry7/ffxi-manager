using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using FFXIManager.Infrastructure;
using FFXIManager.Services;
using FFXIManager.ViewModels.Base;

namespace FFXIManager.ViewModels
{
    public class HeaderViewModel : ViewModelBase, IDisposable
    {
        private readonly IUiDispatcher _ui;
        private readonly ISettingsService _settings;
        private readonly IExternalApplicationService _apps;
        private readonly IPlayOnlineMonitorService _pol;
        private int _runningApps;
        private int _runningChars;
        private string _activeProfile = string.Empty;
        private bool _isDarkTheme;
        private bool _disposed;
        private string _currentSectionTitle = "FFXI Manager";

        public HeaderViewModel(
            IUiDispatcher ui,
            ISettingsService settings,
            IExternalApplicationService apps,
            IPlayOnlineMonitorService pol)
        {
            _ui = ui;
            _settings = settings;
            _apps = apps;
            _pol = pol;

            // Initialize from settings
            try
            {
                var s = _settings.LoadSettings();
                _isDarkTheme = s.IsDarkTheme;
                _activeProfile = s.LastActiveProfileName ?? string.Empty;
            }
            catch { }

            // Subscribe to events
            _apps.ApplicationStatusChanged += (_, __) => _ui.BeginInvoke(UpdateRunningApps);
            _pol.CharacterDetected += (_, __) => _ui.BeginInvoke(UpdateRunningChars);
            _pol.CharacterRemoved += (_, __) => _ui.BeginInvoke(UpdateRunningChars);
            _pol.CharacterUpdated += (_, __) => _ui.BeginInvoke(UpdateRunningChars);

            OpenSettingsCommand = new RelayCommand(OpenSettings);
            ToggleThemeCommand = new RelayCommand(ToggleTheme);
            GlobalSearchCommand = new RelayCommand(ExecuteGlobalSearch);

            // Initial counts
            _ = RefreshAsync();
        }


        public int RunningApps
        {
            get => _runningApps;
            set => SetProperty(ref _runningApps, value);
        }

        public int RunningCharacters
        {
            get => _runningChars;
            set => SetProperty(ref _runningChars, value);
        }

        public string ActiveProfile
        {
            get => _activeProfile;
            set => SetProperty(ref _activeProfile, value);
        }

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set => SetProperty(ref _isDarkTheme, value);
        }

        public string CurrentSectionTitle
        {
            get => _currentSectionTitle;
            set => SetProperty(ref _currentSectionTitle, value);
        }

        public ICommand OpenSettingsCommand { get; }
        public ICommand ToggleThemeCommand { get; }
        public ICommand GlobalSearchCommand { get; }

        public async System.Threading.Tasks.Task RefreshAsync()
        {
            try
            {
                // Apps
                var apps = await _apps.GetApplicationsAsync();
                RunningApps = apps.Count(a => a.IsRunning);
                // Characters
                var chars = await _pol.GetCharactersAsync();
                RunningCharacters = chars?.Count ?? 0;
            }
            catch { }
        }

        private void UpdateRunningApps()
        {
            _ = UpdateRunningAppsAsync();
        }

        private async System.Threading.Tasks.Task UpdateRunningAppsAsync()
        {
            try
            {
                var apps = await _apps.GetApplicationsAsync();
                RunningApps = apps.Count(a => a.IsRunning);
            }
            catch { }
        }

        private void UpdateRunningChars()
        {
            _ = UpdateRunningCharsAsync();
        }

        private async System.Threading.Tasks.Task UpdateRunningCharsAsync()
        {
            try
            {
                var chars = await _pol.GetCharactersAsync();
                RunningCharacters = chars?.Count ?? 0;
            }
            catch { }
        }


        private void OpenSettings()
        {
            try
            {
                var dlg = new Views.DiscoverySettingsDialog();
                dlg.Owner = System.Windows.Application.Current?.MainWindow;
                dlg.DataContext = new ViewModels.DiscoverySettingsViewModel();
                dlg.ShowDialog();
            }
            catch { }
        }

        private void ToggleTheme()
        {
            try
            {
                IsDarkTheme = !IsDarkTheme;
                var s = _settings.LoadSettings();
                s.IsDarkTheme = IsDarkTheme;
                _settings.SaveSettings(s);
                
                // Apply the theme change immediately
                App.ApplyTheme(IsDarkTheme);
            }
            catch { }
        }

        private void ExecuteGlobalSearch()
        {
            // Placeholder for future search flyout
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _apps.ApplicationStatusChanged -= (_, __) => _ui.BeginInvoke(UpdateRunningApps);
            _pol.CharacterDetected -= (_, __) => _ui.BeginInvoke(UpdateRunningChars);
            _pol.CharacterRemoved -= (_, __) => _ui.BeginInvoke(UpdateRunningChars);
            _pol.CharacterUpdated -= (_, __) => _ui.BeginInvoke(UpdateRunningChars);
            GC.SuppressFinalize(this);
        }
    }
}

