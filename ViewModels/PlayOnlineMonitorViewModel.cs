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
            
            // Start monitoring
            _monitorService.StartMonitoring(_cts.Token);
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
                        _monitorService.StartMonitoring(_cts.Token);
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
                var characters = await _monitorService.GetRunningCharactersAsync(_cts.Token);
                
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Characters.Clear();
                    foreach (var character in characters)
                    {
                        Characters.Add(character);
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

                var success = await _monitorService.ActivateCharacterWindowAsync(character, _cts.Token);
                
                if (success)
                {
                    _statusService.SetMessage($"Activated {character.DisplayName}");
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

        private void OnCharacterDetected(object? sender, PlayOnlineCharacter character)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                Characters.Add(character);
                OnPropertyChanged(nameof(CharacterCount));
            });
        }

        private void OnCharacterUpdated(object? sender, PlayOnlineCharacter character)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                // Character object is already updated due to reference
                // Just trigger any UI updates if needed
            });
        }

        private void OnCharacterRemoved(object? sender, int processId)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                var character = Characters.FirstOrDefault(c => c.ProcessId == processId);
                if (character != null)
                {
                    Characters.Remove(character);
                    OnPropertyChanged(nameof(CharacterCount));
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