using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.ViewModels.Base;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows.Input;

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
                // Ensure characters are loaded at open time without blocking UI
                _ = LoadCharactersAsync();
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
                _monitorService.CharacterDetected -= OnCharacterDetected;
                _monitorService.CharacterUpdated -= OnCharacterUpdated;
                _monitorService.CharacterRemoved -= OnCharacterRemoved;
                _monitorService.StopMonitoring();
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