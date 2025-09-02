using FFXIManager.Models;
using FFXIManager.Models.Settings;
using FFXIManager.Services;
using FFXIManager.ViewModels.Base;
using System.Collections.ObjectModel;
using System.Collections.Generic;
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
        private readonly ICharacterOrderingService _characterOrderingService;
        private readonly SemaphoreSlim _updateSemaphore = new(1, 1);
        private bool _isMonitoring = true;
        private bool _autoRefresh = true;
        private bool _disposed;
        private CancellationTokenSource _cts = new();
        private readonly System.Windows.Threading.DispatcherTimer _statusRefreshTimer;

        public PlayOnlineMonitorViewModel(
            IPlayOnlineMonitorService monitorService,
            IStatusMessageService statusService,
            ILoggingService loggingService)
        {
            _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
            _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _characterOrderingService = ServiceLocator.CharacterOrderingService;

            Characters = new ObservableCollection<PlayOnlineCharacter>();

            InitializeCommands();

            // Subscribe to character ordering service changes (the source of truth)
            _characterOrderingService.CharacterCacheUpdated += OnCharacterOrderChanged;
            
            // Load initial character order from service
            _ = LoadCharactersFromServiceAsync();

            // Subscribe to monitor service events for real-time updates
            _monitorService.CharacterDetected += OnCharacterDetected;
            _monitorService.CharacterUpdated += OnCharacterUpdated;
            _monitorService.CharacterRemoved += OnCharacterRemoved;
            
            // Subscribe to hotkey activation service for persistent green border updates
            ServiceLocator.HotkeyActivationService.CharacterActivated += OnCharacterActivatedViaHotkey;
            
            // Start monitoring AFTER UI is ready to receive events
            _monitorService.StartMonitoring();
            
            // **NEW FEATURE**: Start timer to refresh LastActivated status display
            _statusRefreshTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30) // Update every 30 seconds to show "2m ago", "3m ago", etc.
            };
            _statusRefreshTimer.Tick += (s, e) => RefreshStatusDisplay();
            _statusRefreshTimer.Start();
        }

        #region Properties

        public ObservableCollection<PlayOnlineCharacter> Characters { get; }
        
        /// <summary>
        /// Loads characters from the ordering service (source of truth)
        /// </summary>
        private async Task LoadCharactersFromServiceAsync()
        {
            // Prevent multiple simultaneous loads
            if (!await _updateSemaphore.WaitAsync(100))
            {
                return; // Another update is in progress
            }
            
            try
            {
                var orderedCharacters = await _characterOrderingService.GetOrderedCharactersAsync();
                
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Characters.Clear();
                    foreach (var character in orderedCharacters)
                    {
                        Characters.Add(character);
                    }
                    OnPropertyChanged(nameof(CharacterCount));
                    
                    // Refresh command states after UI update
                    RefreshMoveCommandStates();
                });
                
                await _loggingService.LogDebugAsync($"Loaded {orderedCharacters.Count} characters from ordering service", "PlayOnlineMonitorViewModel");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Failed to load characters from ordering service", ex, "PlayOnlineMonitorViewModel");
            }
            finally
            {
                _updateSemaphore.Release();
            }
        }
        
        /// <summary>
        /// Called when character order changes in the service
        /// </summary>
        private void OnCharacterOrderChanged(object? sender, CharacterCacheUpdatedEventArgs e)
        {
            _ = LoadCharactersFromServiceAsync();
        }

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

        // **PERFORMANCE FEEDBACK**: Real-time performance metrics for UI
        public double AverageActivationTimeMs { get; private set; }
        public double LastActivationTimeMs { get; private set; }
        public string PerformanceStatus { get; private set; } = "Optimal";
        public int CacheHitRate { get; private set; }
        public string PerformanceStatusDisplay => $"{PerformanceStatus} | Avg: {AverageActivationTimeMs:F0}ms | Cache: {CacheHitRate}%";

        #endregion

        #region Commands

        public ICommand ActivateCharacterCommand { get; private set; } = null!;
        public ICommand RefreshCharactersCommand { get; private set; } = null!;
        public ICommand ToggleMonitoringCommand { get; private set; } = null!;
        public ICommand ShowCharacterMonitorCommand { get; private set; } = null!;
        public ICommand SwitchToSlotCommand { get; private set; } = null!;
        public ICommand MoveCharacterUpCommand { get; private set; } = null!;
        public ICommand MoveCharacterDownCommand { get; private set; } = null!;

        // In-memory preferred order (session only). Uses CharacterName as the stable identity for stickiness.
        private readonly List<string> _preferredOrder = new();

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

            MoveCharacterUpCommand = new RelayCommandWithParameter<PlayOnlineCharacter>(
                character => MoveCharacter(character, direction: -1),
                character => CanMoveCharacter(character, direction: -1));

            MoveCharacterDownCommand = new RelayCommandWithParameter<PlayOnlineCharacter>(
                character => MoveCharacter(character, direction: +1),
                character => CanMoveCharacter(character, direction: +1));

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

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    _ = _loggingService.LogWarningAsync("Application dispatcher not available during character loading", "PlayOnlineMonitorViewModel");
                    return;
                }
                
                await dispatcher.InvokeAsync(() =>
                {
                    // Ensure newly discovered characters are appended to the session order by CharacterName (stable within session)
                    foreach (var c in characters)
                    {
                        var key = GetCharacterKey(c);
                        if (!string.IsNullOrWhiteSpace(key) && !_preferredOrder.Contains(key))
                        {
                            _preferredOrder.Add(key);
                        }
                    }
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

                // Apply ordering after load/merge
                ApplyPreferredOrder();
                
                // Refresh move command states after loading/merging characters
                RefreshMoveCommandStates();

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
            return character != null; // **SIMPLIFIED**: Allow switching to any running character
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

        #region Private Methods

        private async Task ActivateCharacterAsync(PlayOnlineCharacter character)
        {
            if (character == null) return;

            try
            {
                _statusService.SetMessage($"Activating {character.DisplayName}...");

                // **CONSOLIDATION**: Use the unified activation service for consistency and performance
                var activationService = ServiceLocator.HotkeyActivationService;
                var result = await activationService.ActivateCharacterDirectAsync(character, _cts.Token);

                if (result.Success)
                {
                    _statusService.SetMessage($"Activated {character.DisplayName} ({result.Duration.TotalMilliseconds:F0}ms)");
                    
                    // **USER FEEDBACK**: Update persistent activation indicator
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        // Clear IsLastActivated from all other characters
                        foreach (var c in Characters)
                        {
                            if (c != character)
                                c.IsLastActivated = false;
                        }
                        
                        // Set the activated character as last activated
                        character.IsLastActivated = true;
                    });
                    
                    // **PERFORMANCE FEEDBACK**: Update UI with performance metrics
                    await UpdatePerformanceMetricsAsync();
                }
                else
                {
                    _statusService.SetMessage($"Failed to activate {character.DisplayName}: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                _statusService.SetMessage($"Error activating character: {ex.Message}");
                await _loggingService.LogErrorAsync($"Error activating character {character.DisplayName}", ex, "PlayOnlineMonitorViewModel");
            }
        }

        /// <summary>
        /// Updates the performance metrics displayed in the UI.
        /// </summary>
        private async Task UpdatePerformanceMetricsAsync()
        {
            try
            {
                var activationService = ServiceLocator.HotkeyActivationService;
                var stats = activationService.GetPerformanceStats();
                var cacheStats = ServiceLocator.CharacterOrderingService.GetCacheStatistics();
                
                AverageActivationTimeMs = stats.AverageActivationTimeMs;
                CacheHitRate = (int)cacheStats.HitRate;
                
                PerformanceStatus = stats.AverageActivationTimeMs switch
                {
                    < 50 => "⚡ Excellent",
                    < 100 => "✅ Good", 
                    < 200 => "⚠️ Fair",
                    _ => "🐌 Slow"
                };
                
                OnPropertyChanged(nameof(AverageActivationTimeMs));
                OnPropertyChanged(nameof(PerformanceStatus));
                OnPropertyChanged(nameof(CacheHitRate));
                OnPropertyChanged(nameof(PerformanceStatusDisplay));
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error updating performance metrics", ex, "PlayOnlineMonitorViewModel");
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
                _statusService.SetMessage("Opening character monitor window...");
                
                // Use the new Character Monitor architecture via helper
                Views.CharacterMonitorHelper.ShowCharacterMonitor();
                
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
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                _ = _loggingService.LogWarningAsync("Application dispatcher not available during character detection", "PlayOnlineMonitorViewModel");
                return;
            }
            
            dispatcher.Invoke(() =>
            {
                // Maintain session order stickiness by CharacterName on detection
                var key = GetCharacterKey(character);
                if (!string.IsNullOrWhiteSpace(key) && !_preferredOrder.Contains(key))
                {
                    _preferredOrder.Add(key);
                }
                // Prevent duplicates
                var existing = Characters.FirstOrDefault(c => c.ProcessId == character.ProcessId && c.WindowHandle == character.WindowHandle);
                if (existing == null)
                {
                    Characters.Add(character);
                    OnPropertyChanged(nameof(CharacterCount));
                    OnPropertyChanged(nameof(MonitoringStatus));
                    _statusService.SetMessage($"PlayOnline character detected: {character.DisplayName}");
                    
                    // **IMMEDIATE AVAILABILITY**: Ensure new characters are immediately switchable
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // Force immediate cache invalidation so hotkey system sees new character
                            await ServiceLocator.CharacterOrderingService.InvalidateCacheAsync();
                            
                            // Refresh hotkey mappings so new character gets proper hotkey assignment
                            await ServiceLocator.HotkeyMappingService.RefreshMappingsAsync();
                            
                            await _loggingService.LogInfoAsync($"✅ Character '{character.DisplayName}' is now available for hotkey switching", "PlayOnlineMonitorViewModel");
                        }
                        catch (Exception ex)
                        {
                            await _loggingService.LogErrorAsync($"Error making new character '{character.DisplayName}' available for switching", ex, "PlayOnlineMonitorViewModel");
                        }
                    });
                }
                // Re-apply ordering after changes
                ApplyPreferredOrder();
                
                // Refresh move command states since collection changed
                RefreshMoveCommandStates();
            });
        }

        private void OnCharacterUpdated(object? sender, PlayOnlineCharacterEventArgs e)
        {
            var updatedCharacter = e.Character;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                _ = _loggingService.LogWarningAsync("Application dispatcher not available during character update", "PlayOnlineMonitorViewModel");
                return;
            }
            
            dispatcher.Invoke(() =>
            {
                // Attempt to preserve slot by CharacterName on update (handle/name changes)
                var key = GetCharacterKey(updatedCharacter);
                if (!string.IsNullOrWhiteSpace(key) && !_preferredOrder.Contains(key))
                {
                    _preferredOrder.Add(key);
                }
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
                    // Note: LastActivated is managed by HotkeyActivationService, not by monitor updates
                }
                else
                {
                    // Character truly not found - might be a new window, add it
                    _loggingService.LogInfoAsync($"[UI] New window detected for existing process - PID: {updatedCharacter.ProcessId}, Handle: 0x{updatedCharacter.WindowHandle.ToInt64():X}, Title: '{updatedCharacter.WindowTitle}'",
                        "PlayOnlineMonitorViewModel");
                    Characters.Add(updatedCharacter);
                    OnPropertyChanged(nameof(CharacterCount));
                }
                // Re-apply ordering after update
                ApplyPreferredOrder();
                
                // Refresh move command states since collection or ordering may have changed
                RefreshMoveCommandStates();
            });
        }

        private void OnCharacterRemoved(object? sender, PlayOnlineCharacterEventArgs e)
        {
            var processId = e.Character.ProcessId;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                _ = _loggingService.LogWarningAsync("Application dispatcher not available during character removal", "PlayOnlineMonitorViewModel");
                return;
            }
            
            dispatcher.Invoke(() =>
            {
                // Keep sticky order for the session: do NOT remove the key from _preferredOrder here.
                // This keeps positions stable while processes restart.
                var character = Characters.FirstOrDefault(c => c.ProcessId == processId);
                if (character != null)
                {
                    Characters.Remove(character);
                    OnPropertyChanged(nameof(CharacterCount));
                    OnPropertyChanged(nameof(MonitoringStatus));
                    _statusService.SetMessage($"PlayOnline character removed: {character.DisplayName}");
                }

                // Re-apply ordering after removal
                ApplyPreferredOrder();
                
                // Refresh move command states since collection changed
                RefreshMoveCommandStates();
            });
        }

        /// <summary>
        /// Handles character activation from both hotkey and UI sources to maintain persistent green border state.
        /// </summary>
        private void OnCharacterActivatedViaHotkey(object? sender, HotkeyActivationResult e)
        {
            // Only handle successful activations
            if (!e.Success || e.Character == null) return;
            
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                _ = _loggingService.LogWarningAsync("Application dispatcher not available during hotkey activation", "PlayOnlineMonitorViewModel");
                return;
            }
            
            dispatcher.Invoke(() =>
            {
                // Find the activated character in our collection
                var activatedCharacter = Characters.FirstOrDefault(c => c.ProcessId == e.Character.ProcessId);
                if (activatedCharacter != null)
                {
                    // Clear IsLastActivated from all other characters
                    foreach (var c in Characters)
                    {
                        if (c != activatedCharacter)
                            c.IsLastActivated = false;
                    }
                    
                    // Set the activated character as last activated (persistent green border)
                    activatedCharacter.IsLastActivated = true;
                    
                    _ = _loggingService.LogDebugAsync($"Updated persistent green border: {activatedCharacter.DisplayName} (activated via {e.Source})", "PlayOnlineMonitorViewModel");
                }
            });
        }

        #endregion

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                // Unsubscribe from events
                _characterOrderingService.CharacterCacheUpdated -= OnCharacterOrderChanged;
                _monitorService.CharacterDetected -= OnCharacterDetected;
                _monitorService.CharacterUpdated -= OnCharacterUpdated;
                _monitorService.CharacterRemoved -= OnCharacterRemoved;
                ServiceLocator.HotkeyActivationService.CharacterActivated -= OnCharacterActivatedViaHotkey;
                
                // Stop monitoring
                _monitorService.StopMonitoring();

                // **NEW FEATURE**: Stop status refresh timer
                _statusRefreshTimer?.Stop();

                _updateSemaphore?.Dispose();
                _cts.Cancel();
                _cts.Dispose();
                _disposed = true;
            }
        }

        private bool CanMoveCharacter(PlayOnlineCharacter? character, int direction)
        {
            if (character == null || Characters.Count <= 1) return false;
            
            // Find the character's current position in the UI (should reflect service state)
            var currentIndex = Characters.IndexOf(character);
            if (currentIndex < 0) return false;
            
            // Check if movement in the specified direction is valid
            if (direction < 0 && currentIndex == 0)
                return false; // Can't move up from first position
                
            if (direction > 0 && currentIndex == Characters.Count - 1)
                return false; // Can't move down from last position
            
            return true;
        }

        private async void MoveCharacter(PlayOnlineCharacter? character, int direction)
        {
            if (character == null) return;
            
            try
            {
                // Get current character order from service (source of truth)
                var serviceCharacters = await _characterOrderingService.GetOrderedCharactersAsync();
                
                // Find the character's current position in the service
                var currentIndex = serviceCharacters.FindIndex(c => c.ProcessId == character.ProcessId);
                if (currentIndex < 0)
                {
                    await _loggingService.LogWarningAsync($"Character {character.DisplayName} not found in service", "PlayOnlineMonitorViewModel");
                    return;
                }

                // Calculate the target index
                var targetIndex = currentIndex + direction;
                
                // Validate target index
                if (targetIndex < 0 || targetIndex >= serviceCharacters.Count)
                {
                    await _loggingService.LogDebugAsync($"Cannot move {character.DisplayName} to slot {targetIndex} (out of range)", "PlayOnlineMonitorViewModel");
                    return;
                }
                
                // Update the service (source of truth) - UI will automatically reflect the change
                var success = await _characterOrderingService.MoveCharacterToSlotAsync(character, targetIndex);
                
                if (!success)
                {
                    await _loggingService.LogWarningAsync($"Failed to move {character.DisplayName} to slot {targetIndex}", "PlayOnlineMonitorViewModel");
                }
                else
                {
                    await _loggingService.LogInfoAsync($"Successfully moved {character.DisplayName} to slot {targetIndex}", "PlayOnlineMonitorViewModel");
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error moving character {character.DisplayName}", ex, "PlayOnlineMonitorViewModel");
            }
            finally
            {
                // Refresh command states
                RefreshMoveCommandStates();
            }
        }

        private void ApplyPreferredOrder()
        {
            // Sort the Characters collection to match _preferredOrder (stable for unmatched)
            if (Characters.Count <= 1) return;

            var keyIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < _preferredOrder.Count; i++)
            {
                keyIndex[_preferredOrder[i]] = i;
            }

            var sorted = Characters
                .OrderBy(c => keyIndex.TryGetValue(GetCharacterKey(c), out var k) ? k : int.MaxValue)
                .ThenBy(c => GetCharacterKey(c))
                .ToList();

            // Reorder in-place to minimize UI churn
            for (int target = 0; target < sorted.Count; target++)
            {
                var item = sorted[target];
                var currentIndex = Characters.IndexOf(item);
                if (currentIndex != target && currentIndex >= 0)
                {
                    Characters.Move(currentIndex, target);
                }
            }
        }

        private static string GetCharacterKey(PlayOnlineCharacter? c)
        {
            // Use DisplayName if available, then CharacterName; never return null.
            return (c?.DisplayName ?? c?.CharacterName) ?? string.Empty;
        }

        private void RefreshMoveCommandStates()
        {
            // Notify WPF that the CanExecute state of move commands may have changed
            // This will cause the UI to re-evaluate button enabled/disabled states for all characters
            // Note: This is called whenever the Characters collection changes, ensuring all move buttons
            // reflect the current valid positions. While this refreshes all commands globally,
            // it's acceptable for small-to-medium character counts (1-12 characters) and ensures
            // UI consistency. For larger lists, consider per-character command refresh optimization.
            
            if (MoveCharacterUpCommand is RelayCommandWithParameter<PlayOnlineCharacter> upCommand)
            {
                upCommand.RaiseCanExecuteChanged();
            }
            
            if (MoveCharacterDownCommand is RelayCommandWithParameter<PlayOnlineCharacter> downCommand)
            {
                downCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>
        /// **NEW FEATURE**: Refreshes the LastActivated status display for all characters
        /// </summary>
        private void RefreshStatusDisplay()
        {
            // Force all characters to refresh their status properties
            foreach (var character in Characters)
            {
                character.OnPropertyChanged(nameof(character.StatusText));
                character.OnPropertyChanged(nameof(character.StatusColor));
                character.OnPropertyChanged(nameof(character.StatusBrush));
                character.OnPropertyChanged(nameof(character.IsRecentlyActivated));
            }
        }

        // REMOVED: RegisterAsCharacterOrderingProvider - no longer needed
        // The CharacterOrderingService is now the source of truth

        // Implement IDisposable
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
