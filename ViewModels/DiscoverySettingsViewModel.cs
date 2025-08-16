using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using FFXIManager.Infrastructure;
using FFXIManager.Models.Settings;
using FFXIManager.Services;
using FFXIManager.ViewModels;
using FFXIManager.Views;

namespace FFXIManager.ViewModels
{
    public class DiscoverySettingsViewModel : Base.ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private readonly Dictionary<KeyboardShortcutConfig, bool> _originalEnabledStates = new();
        
        /// <summary>
        /// Static event fired when hotkey settings are changed and saved.
        /// CharacterMonitorWindow instances can subscribe to this to refresh their hotkeys.
        /// </summary>
        public static event EventHandler? HotkeySettingsChanged;

        public DiscoverySettingsViewModel() : this(ServiceLocator.SettingsService) {}

        public DiscoverySettingsViewModel(ISettingsService settingsService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            CharacterHotkeys = new ObservableCollection<KeyboardShortcutConfig>();
            LoadFromSettings();
            SaveCommand = new RelayCommand(() => Save());
            CancelCommand = new RelayCommand(() => { /* handled by dialog */ });
            EditHotkeyCommand = new RelayCommandWithParameter<KeyboardShortcutConfig>(EditHotkey);
        }


        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand EditHotkeyCommand { get; }

        private bool _enableDiagnostics;
        public bool EnableDiagnostics
        {
            get => _enableDiagnostics;
            set { _enableDiagnostics = value; OnPropertyChanged(); }
        }

        private bool _verboseLogging;
        public bool VerboseLogging
        {
            get => _verboseLogging;
            set { _verboseLogging = value; OnPropertyChanged(); }
        }

        private int _maxLogEntries;
        public int MaxLogEntries
        {
            get => _maxLogEntries;
            set { _maxLogEntries = value; OnPropertyChanged(); }
        }
        
        private bool _enableHotkeys;
        public bool EnableHotkeys
        {
            get => _enableHotkeys;
            set { _enableHotkeys = value; OnPropertyChanged(); }
        }
        
        public ObservableCollection<KeyboardShortcutConfig> CharacterHotkeys { get; }


        private void LoadFromSettings()
        {
            // Use the cached settings property from SettingsService
            var settings = _settingsService.LoadSettings();

            // Diagnostics
            EnableDiagnostics = settings.Diagnostics?.EnableDiagnostics ?? false;
            VerboseLogging = settings.Diagnostics?.VerboseLogging ?? false;
            MaxLogEntries = settings.Diagnostics?.MaxLogEntries ?? 1000;
            
            // Character Hotkeys  
            EnableHotkeys = settings.CharacterSwitchShortcuts.Any(s => s.IsEnabled);
            CharacterHotkeys.Clear();
            
            // If no shortcuts configured, create defaults
            if (settings.CharacterSwitchShortcuts.Count == 0)
            {
                var defaultShortcuts = ApplicationSettings.GetDefaultShortcuts();
                foreach (var shortcut in defaultShortcuts)
                {
                    CharacterHotkeys.Add(shortcut);
                }
            }
            else
            {
                foreach (var shortcut in settings.CharacterSwitchShortcuts)
                {
                    CharacterHotkeys.Add(shortcut);
                }
            }
        }

        public void Save()
        {
            // Use the specific update method instead of saving the entire settings object
            var validMaxLogEntries = MaxLogEntries > 0 ? MaxLogEntries : 1000;
            _settingsService.UpdateDiagnostics(EnableDiagnostics, VerboseLogging, validMaxLogEntries);
            
            // Save keyboard shortcuts
            var settings = _settingsService.LoadSettings();
            settings.CharacterSwitchShortcuts.Clear();
            settings.CharacterSwitchShortcuts.AddRange(CharacterHotkeys);
            
            // Handle EnableHotkeys toggle properly to preserve individual shortcut states
            if (!EnableHotkeys)
            {
                // Store original enabled states only if we haven't already stored them
                // This prevents losing states on multiple toggles
                if (_originalEnabledStates.Count == 0)
                {
                    foreach (var shortcut in settings.CharacterSwitchShortcuts)
                    {
                        _originalEnabledStates[shortcut] = shortcut.IsEnabled;
                    }
                }
                
                // Disable all shortcuts
                foreach (var shortcut in settings.CharacterSwitchShortcuts)
                {
                    shortcut.IsEnabled = false;
                }
            }
            else
            {
                // Restore original enabled states if available
                foreach (var shortcut in settings.CharacterSwitchShortcuts)
                {
                    if (_originalEnabledStates.TryGetValue(shortcut, out var wasEnabled))
                    {
                        shortcut.IsEnabled = wasEnabled;
                    }
                    // If no stored state, leave shortcut as-is (preserves user's individual toggles)
                }
                
                // Clear the stored states after successful restoration
                _originalEnabledStates.Clear();
            }
            
            _settingsService.SaveSettings(settings);
            
            // Notify any listening CharacterMonitorWindow instances that hotkey settings have changed
            HotkeySettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        
        private void EditHotkey(KeyboardShortcutConfig shortcut)
        {
            if (shortcut == null) return;
            
            var dialog = new HotkeyEditDialog(shortcut);
            var result = dialog.ShowDialog();
            
            if (result == true && dialog.EditedShortcut != null)
            {
                // Update the shortcut in place
                var index = CharacterHotkeys.IndexOf(shortcut);
                if (index >= 0)
                {
                    CharacterHotkeys[index] = dialog.EditedShortcut;
                }
            }
        }
    }
}
