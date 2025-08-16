using FFXIManager.Models;
using FFXIManager.Models.Settings;
using FFXIManager.Services;
using FFXIManager.ViewModels.Base;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows.Input;
using FFXIManager.Infrastructure;

namespace FFXIManager.ViewModels
{
    /// <summary>
    /// ViewModel for managing PlayOnline character monitoring
    /// </summary>
    public class PlayOnlineMonitorViewModel : ViewModelBase, IDisposable
    {
        private readonly IPlayOnlineMonitorService _monitorService;
        private readonly IStatusMessageService _statusService;
        private readonly ILoggingService _loggingService;
        private bool _isMonitoring = true;
        private bool _autoRefresh = true;
        private bool _disposed;
        private CancellationTokenSource _cts = new();

        public PlayOnlineMonitorViewModel(
            IPlayOnlineMonitorService monitorService,
            IStatusMessageService statusService,
            ILoggingService loggingService)
        {
            _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
            _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));

            Characters = new ObservableCollection<PlayOnlineCharacter>();
            
            // Subscribe to monitor events
            _monitorService.CharacterDetected += OnCharacterDetected;
            _monitorService.CharacterUpdated += OnCharacterUpdated;
            _monitorService.CharacterRemoved += OnCharacterRemoved;

            InitializeCommands();
            
            // Initialize global hotkey handling
            InitializeHotkeyHandling();
            
            // Start monitoring AFTER UI is ready to receive events
            _monitorService.StartMonitoring();
        }

        #region Properties

        public ObservableCollection<PlayOnlineCharacter> Characters { get; }

        public bool IsMonitoring
        {
            get => _isMonitoring;
            set
            {
                if (SetProperty(ref _isMonitoring, value))
                {
                    if (value)
                        _monitorService.StartMonitoring();
                    else
                        _monitorService.StopMonitoring();
                    
                    ((RelayCommand)ToggleMonitoringCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public bool AutoRefresh
        {
            get => _autoRefresh;
            set => SetProperty(ref _autoRefresh, value);
        }

        public int CharacterCount => Characters.Count;

        public string MonitoringStatus => IsMonitoring ? "Monitoring Active" : "Monitoring Paused";

        #endregion

        #region Commands

        public ICommand ActivateCharacterCommand { get; private set; } = null!;
        public ICommand RefreshCharactersCommand { get; private set; } = null!;
        public ICommand ToggleMonitoringCommand { get; private set; } = null!;
        public ICommand ShowCharacterMonitorCommand { get; private set; } = null!;
        public ICommand SwitchToSlotCommand { get; private set; } = null!;

        private void InitializeCommands()
        {
            ActivateCharacterCommand = new RelayCommandWithParameter<PlayOnlineCharacter>(
                async character => await ActivateCharacterAsync(character),
                character => character != null);

            RefreshCharactersCommand = new RelayCommand(
                async () => await RefreshCharactersAsync());

            ToggleMonitoringCommand = new RelayCommand(
                () => IsMonitoring = !IsMonitoring);

            ShowCharacterMonitorCommand = new RelayCommand(
                () => ShowCharacterMonitorDialog());

            SwitchToSlotCommand = new RelayCommandWithParameter<int>(
                async slotIndex => await SwitchToSlotAsync(slotIndex),
                slotIndex => CanSwitchToSlot(slotIndex));

            // Prime the list on startup so the mini-UI shows existing characters
            _ = LoadCharactersAsync();
        }

        #endregion

        #region Public Methods

        public async Task LoadCharactersAsync()
        {
            try
            {
                var characters = await _monitorService.GetCharactersAsync();
                
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // If monitoring is active, merge characters instead of clearing
                    // This prevents overwriting real-time event updates during manual refresh
                    if (IsMonitoring)
                    {
                        // Add new characters that aren't already in the collection
                        foreach (var character in characters)
                        {
                            var existing = Characters.FirstOrDefault(c => c.ProcessId == character.ProcessId && c.WindowHandle == character.WindowHandle);
                            if (existing == null)
                            {
                                Characters.Add(character);
                            }
                        }
                        
                        // Remove characters that are no longer running
                        var toRemove = Characters.Where(c => !characters.Any(ch => ch.ProcessId == c.ProcessId && ch.WindowHandle == c.WindowHandle)).ToList();
                        foreach (var character in toRemove)
                        {
                            Characters.Remove(character);
                        }
                    }
                    else
                    {
                        // If monitoring is disabled, do a full refresh
                        Characters.Clear();
                        foreach (var character in characters)
                        {
                            Characters.Add(character);
                        }
                    }
                });

                OnPropertyChanged(nameof(CharacterCount));
                _statusService.SetMessage($"Found {characters.Count} PlayOnline character(s)");
            }
            catch (Exception ex)
            {
                _statusService.SetMessage($"Error loading characters: {ex.Message}");
                await _loggingService.LogErrorAsync("Error loading characters", ex, "PlayOnlineMonitorViewModel");
            }
        }

        #endregion

        #region Public Methods - Character Switching

        /// <summary>
        /// Switches to the character at the specified slot index
        /// </summary>
        /// <param name="slotIndex">0-based slot index</param>
        public async Task SwitchToSlotAsync(int slotIndex)
        {
            if (!CanSwitchToSlot(slotIndex)) return;

            var character = Characters.ElementAtOrDefault(slotIndex);
            if (character != null)
            {
                await ActivateCharacterAsync(character);
            }
        }

        /// <summary>
        /// Checks if switching to the specified slot is possible
        /// </summary>
        /// <param name="slotIndex">0-based slot index</param>
        /// <returns>True if the slot can be switched to</returns>
        public bool CanSwitchToSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= Characters.Count) return false;
            
            var character = Characters.ElementAtOrDefault(slotIndex);
            return character != null && !character.IsActive;
        }

        #endregion

        #region Public Methods - Hotkey Support

        /// <summary>
        /// Gets the hotkey display text for a character based on its position in the Characters collection
        /// </summary>
        /// <param name="character">The character to get the hotkey for</param>
        /// <returns>The hotkey display text (e.g., "Win+F1") or "No Hotkey" if none configured</returns>
        public string GetHotkeyForCharacter(PlayOnlineCharacter character)
        {
            if (character == null) return "No Hotkey";

            try
            {
                // Find the character's index in the collection
                var index = Characters.IndexOf(character);
                if (index < 0) return "No Hotkey";

                // Get the hotkey settings
                var settingsService = ServiceLocator.SettingsService;
                var settings = settingsService.LoadSettings();

                // Find the corresponding hotkey for this slot index
                var shortcut = settings.CharacterSwitchShortcuts.FirstOrDefault(s => s.SlotIndex == index);
                if (shortcut != null && shortcut.IsEnabled)
                {
                    return shortcut.DisplayText;
                }

                return "No Hotkey";
            }
            catch
            {
                return "No Hotkey";
            }
        }

        #endregion

        #region Hotkey Management

        /// <summary>
        /// Initializes global hotkey handling for character switching
        /// </summary>
        private void InitializeHotkeyHandling()
        {
            try
            {
                // Subscribe to the global hotkey manager
                GlobalHotkeyManager.Instance.HotkeyPressed += OnGlobalHotkeyPressed;
                
                // Register hotkeys from settings
                GlobalHotkeyManager.Instance.RegisterHotkeysFromSettings();
                
                // Subscribe to hotkey settings changes
                DiscoverySettingsViewModel.HotkeySettingsChanged += OnHotkeySettingsChanged;
                
                _loggingService.LogInfoAsync("PlayOnlineMonitorViewModel initialized with global hotkey support", "PlayOnlineMonitorViewModel");
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync("Error initializing hotkey handling", ex, "PlayOnlineMonitorViewModel");
            }
        }

        /// <summary>
        /// Handles global hotkey press events
        /// </summary>
        private void OnGlobalHotkeyPressed(object? sender, HotkeyPressedEventArgs e)
        {
            try
            {
                // Convert hotkey ID back to slot index
                int slotIndex = e.HotkeyId - 1000; // Subtract the offset
                
                // Execute the switch command on the UI thread
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    if (SwitchToSlotCommand.CanExecute(slotIndex))
                    {
                        SwitchToSlotCommand.Execute(slotIndex);
                        
                        // Get character name for feedback
                        var character = Characters.ElementAtOrDefault(slotIndex);
                        var characterName = character?.DisplayName ?? $"Slot {slotIndex + 1}";
                        
                        _statusService.SetMessage($"Switched to character: {characterName}");
                        _loggingService.LogInfoAsync($"✓ Switched to character via hotkey: {characterName}", "PlayOnlineMonitorViewModel");
                    }
                    else
                    {
                        _loggingService.LogWarningAsync($"⚠ Cannot switch to slot {slotIndex + 1} - slot empty or character is already active", "PlayOnlineMonitorViewModel");
                        _statusService.SetMessage($"Cannot switch to slot {slotIndex + 1}");
                    }
                });
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync("Error handling global hotkey press", ex, "PlayOnlineMonitorViewModel");
            }
        }

        /// <summary>
        /// Handles hotkey settings changes
        /// </summary>
        private void OnHotkeySettingsChanged(object? sender, EventArgs e)
        {
            try
            {
                // Refresh hotkeys when settings change
                GlobalHotkeyManager.Instance.RefreshHotkeys();
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync("Error refreshing hotkeys after settings change", ex, "PlayOnlineMonitorViewModel");
            }
        }

        #endregion

        #region Private Methods

        private async Task ActivateCharacterAsync(PlayOnlineCharacter character)
        {
            if (character == null) return;

            try
            {
                _statusService.SetMessage($"Activating {character.DisplayName}...");
                
                // Simply activate the window - the monitoring service will detect the change
                // and fire events that will update our UI automatically
                var success = await _monitorService.ActivateCharacterWindowAsync(character, _cts.Token);
                
                if (success)
                {
                    _statusService.SetMessage($"Activated {character.DisplayName}");
                    // The UnifiedMonitoringService will detect the active window change within 500ms
                    // and fire ProcessUpdated events that will update our Characters collection
                }
                else
                {
                    _statusService.SetMessage($"Failed to activate {character.DisplayName}");
                }
            }
            catch (Exception ex)
            {
                _statusService.SetMessage($"Error activating character: {ex.Message}");
                await _loggingService.LogErrorAsync($"Error activating character {character.DisplayName}", ex, "PlayOnlineMonitorViewModel");
            }
        }

        private async Task RefreshCharactersAsync()
        {
            await LoadCharactersAsync();
        }

        private void ShowCharacterMonitorDialog()
        {
            try
            {
                var monitorWindow = new Views.CharacterMonitorWindow(this);
                
                _statusService.SetMessage("Opening character monitor window...");
                // The pop-out window should use the same real-time Characters collection
                // No need to call LoadCharactersAsync() as it can interfere with real-time updates
                monitorWindow.Show(); // Non-modal; no Owner so the main window won't be forced to front
                
                _statusService.SetMessage("Character monitor window opened");
            }
            catch (Exception ex)
            {
                _statusService.SetMessage($"Error opening character monitor: {ex.Message}");
                _loggingService.LogErrorAsync("Error opening character monitor window", ex, "PlayOnlineMonitorViewModel");
            }
        }

        private void OnCharacterDetected(object? sender, PlayOnlineCharacterEventArgs e)
        {
            var character = e.Character;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                // Prevent duplicates
                var existing = Characters.FirstOrDefault(c => c.ProcessId == character.ProcessId && c.WindowHandle == character.WindowHandle);
                if (existing == null)
                {
                    Characters.Add(character);
                    OnPropertyChanged(nameof(CharacterCount));
                    OnPropertyChanged(nameof(MonitoringStatus));
                    _statusService.SetMessage($"PlayOnline character detected: {character.DisplayName}");
                }
            });
        }

        private void OnCharacterUpdated(object? sender, PlayOnlineCharacterEventArgs e)
        {
            var updatedCharacter = e.Character;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                // First try to find by PID and window handle (exact match)
                var existing = Characters.FirstOrDefault(c => c.ProcessId == updatedCharacter.ProcessId && c.WindowHandle == updatedCharacter.WindowHandle);
                
                // If not found by handle, the window handle may have changed - find by PID only
                // This handles cases where the game creates new windows or changes handles
                if (existing == null)
                {
                    existing = Characters.FirstOrDefault(c => c.ProcessId == updatedCharacter.ProcessId);
                    if (existing != null)
                    {
                        _loggingService.LogInfoAsync($"[UI] Window handle changed for PID {updatedCharacter.ProcessId}: 0x{existing.WindowHandle.ToInt64():X} -> 0x{updatedCharacter.WindowHandle.ToInt64():X}", 
                            "PlayOnlineMonitorViewModel");
                        // Update the handle since it changed
                        existing.WindowHandle = updatedCharacter.WindowHandle;
                    }
                }
                
                if (existing != null)
                {
                    _loggingService.LogInfoAsync($"[UI] Updating character - Old Title: '{existing.WindowTitle}', New Title: '{updatedCharacter.WindowTitle}'", 
                        "PlayOnlineMonitorViewModel");
                    
                    // Update the properties - the setters will trigger property change notifications
                    existing.WindowTitle = updatedCharacter.WindowTitle;
                    existing.CharacterName = updatedCharacter.CharacterName;
                    existing.ServerName = updatedCharacter.ServerName;
                    existing.LastSeen = updatedCharacter.LastSeen;
                    existing.IsActive = updatedCharacter.IsActive;
                }
                else
                {
                    // Character truly not found - might be a new window, add it
                    _loggingService.LogInfoAsync($"[UI] New window detected for existing process - PID: {updatedCharacter.ProcessId}, Handle: 0x{updatedCharacter.WindowHandle.ToInt64():X}, Title: '{updatedCharacter.WindowTitle}'", 
                        "PlayOnlineMonitorViewModel");
                    Characters.Add(updatedCharacter);
                    OnPropertyChanged(nameof(CharacterCount));
                }
            });
        }

        private void OnCharacterRemoved(object? sender, PlayOnlineCharacterEventArgs e)
        {
            var processId = e.Character.ProcessId;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var character = Characters.FirstOrDefault(c => c.ProcessId == processId);
                if (character != null)
                {
                    Characters.Remove(character);
                    OnPropertyChanged(nameof(CharacterCount));
                    OnPropertyChanged(nameof(MonitoringStatus));
                    _statusService.SetMessage($"PlayOnline character removed: {character.DisplayName}");
                }
            });
        }

        #endregion

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                // Unsubscribe from monitor events
                _monitorService.CharacterDetected -= OnCharacterDetected;
                _monitorService.CharacterUpdated -= OnCharacterUpdated;
                _monitorService.CharacterRemoved -= OnCharacterRemoved;
                _monitorService.StopMonitoring();
                
                // Unsubscribe from hotkey events
                GlobalHotkeyManager.Instance.HotkeyPressed -= OnGlobalHotkeyPressed;
                DiscoverySettingsViewModel.HotkeySettingsChanged -= OnHotkeySettingsChanged;
                
                _cts.Cancel();
                _cts.Dispose();
                _disposed = true;
            }
        }

        // Implement IDisposable
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}