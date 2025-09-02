using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FFXIManager.Infrastructure;
using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.ViewModels.Base;

namespace FFXIManager.ViewModels.CharacterMonitor
{
    /// <summary>
    /// ViewModel that manages the collection of characters in the monitor.
    /// Handles character operations, ordering, and activation.
    /// </summary>
    public class CharacterCollectionViewModel : ViewModelBase, IDisposable
    {
        private readonly IPlayOnlineMonitorService _monitorService;
        private readonly ICharacterOrderingService _orderingService;
        private readonly IHotkeyActivationService _activationService;
        private readonly IStatusMessageService _statusService;
        private readonly ILoggingService _loggingService;
        
        private readonly SemaphoreSlim _updateSemaphore = new(1, 1);
        private readonly DispatcherTimer _statusRefreshTimer;
        private CancellationTokenSource _cts = new();
        private bool _disposed;

        public CharacterCollectionViewModel(
            IPlayOnlineMonitorService monitorService,
            ICharacterOrderingService orderingService,
            IHotkeyActivationService activationService,
            IStatusMessageService statusService,
            ILoggingService loggingService)
        {
            _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
            _orderingService = orderingService ?? throw new ArgumentNullException(nameof(orderingService));
            _activationService = activationService ?? throw new ArgumentNullException(nameof(activationService));
            _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            
            Characters = new ObservableCollection<CharacterItemViewModel>();
            Characters.CollectionChanged += OnCharactersCollectionChanged;
            
            InitializeCommands();
            SubscribeToServices();
            
            // Start status refresh timer
            _statusRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _statusRefreshTimer.Tick += (s, e) => RefreshStatusDisplays();
            _statusRefreshTimer.Start();
            
            // Load initial characters
            _ = LoadCharactersAsync();
        }

        #region Properties

        /// <summary>
        /// Observable collection of character view models
        /// </summary>
        public ObservableCollection<CharacterItemViewModel> Characters { get; }

        /// <summary>
        /// Total number of characters
        /// </summary>
        public int CharacterCount => Characters.Count;

        /// <summary>
        /// Whether any characters are running
        /// </summary>
        public bool HasRunningCharacters => Characters.Any(c => c.IsRunning);

        /// <summary>
        /// Performance metrics
        /// </summary>
        public double AverageActivationTimeMs { get; private set; }
        public int CacheHitRate { get; private set; }
        public string PerformanceStatus { get; private set; } = "Ready";

        #endregion

        #region Commands

        public ICommand RefreshCommand { get; private set; } = null!;
        public ICommand ActivateCharacterCommand { get; private set; } = null!;
        public ICommand MoveCharacterUpCommand { get; private set; } = null!;
        public ICommand MoveCharacterDownCommand { get; private set; } = null!;

        private void InitializeCommands()
        {
            RefreshCommand = new RelayCommand(async () => await RefreshCharactersAsync());
            
            ActivateCharacterCommand = new RelayCommandWithParameter<CharacterItemViewModel>(
                async vm => await ActivateCharacterAsync(vm),
                vm => vm != null && vm.IsRunning && !vm.IsActivating);
            
            MoveCharacterUpCommand = new RelayCommandWithParameter<CharacterItemViewModel>(
                async vm => await MoveCharacterAsync(vm, -1),
                vm => vm != null && vm.CanMoveUp);
            
            MoveCharacterDownCommand = new RelayCommandWithParameter<CharacterItemViewModel>(
                async vm => await MoveCharacterAsync(vm, 1),
                vm => vm != null && vm.CanMoveDown);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Activates a character by slot index (for hotkey support)
        /// </summary>
        public async Task<bool> ActivateCharacterBySlotAsync(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= Characters.Count)
                return false;
            
            var characterVm = Characters[slotIndex];
            return await ActivateCharacterAsync(characterVm);
        }

        /// <summary>
        /// Refreshes the character list
        /// </summary>
        public async Task RefreshCharactersAsync()
        {
            await LoadCharactersAsync();
        }

        #endregion

        #region Private Methods - Character Operations

        private async Task<bool> ActivateCharacterAsync(CharacterItemViewModel characterVm)
        {
            if (characterVm == null || characterVm.IsActivating)
                return false;
            
            try
            {
                characterVm.IsActivating = true;
                _statusService.SetMessage($"Activating {characterVm.DisplayName}...");
                
                var result = await _activationService.ActivateCharacterDirectAsync(
                    characterVm.Character, 
                    _cts.Token);
                
                if (result.Success)
                {
                    // Update last activated state
                    UpdateLastActivatedCharacter(characterVm);
                    
                    _statusService.SetMessage(
                        $"Activated {characterVm.DisplayName} ({result.Duration.TotalMilliseconds:F0}ms)");
                    
                    // Update performance metrics
                    await UpdatePerformanceMetricsAsync();
                    
                    return true;
                }
                else
                {
                    _statusService.SetMessage(
                        $"Failed to activate {characterVm.DisplayName}: {result.ErrorMessage}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    $"Error activating character {characterVm.DisplayName}", 
                    ex, 
                    "CharacterCollectionViewModel");
                return false;
            }
            finally
            {
                characterVm.IsActivating = false;
            }
        }

        private async Task MoveCharacterAsync(CharacterItemViewModel characterVm, int direction)
        {
            if (characterVm == null)
                return;
            
            var currentIndex = Characters.IndexOf(characterVm);
            if (currentIndex < 0)
                return;
            
            var targetIndex = currentIndex + direction;
            if (targetIndex < 0 || targetIndex >= Characters.Count)
                return;
            
            // Update in the ordering service (source of truth)
            var success = await _orderingService.MoveCharacterToSlotAsync(
                characterVm.Character, 
                targetIndex);
            
            if (success)
            {
                // Move in our collection
                Characters.Move(currentIndex, targetIndex);
                
                // Update slot indices
                UpdateSlotIndices();
                
                await _loggingService.LogInfoAsync(
                    $"Moved {characterVm.DisplayName} to slot {targetIndex + 1}", 
                    "CharacterCollectionViewModel");
            }
        }

        private void UpdateLastActivatedCharacter(CharacterItemViewModel activated)
        {
            foreach (var vm in Characters)
            {
                vm.IsLastActivated = (vm == activated);
            }
        }

        private void UpdateSlotIndices()
        {
            for (int i = 0; i < Characters.Count; i++)
            {
                Characters[i].SlotIndex = i;
                Characters[i].CanMoveDown = (i < Characters.Count - 1);
            }
        }

        private void RefreshStatusDisplays()
        {
            foreach (var vm in Characters)
            {
                vm.RefreshStatusDisplay();
            }
        }

        private async Task UpdatePerformanceMetricsAsync()
        {
            try
            {
                var stats = _activationService.GetPerformanceStats();
                var cacheStats = _orderingService.GetCacheStatistics();
                
                AverageActivationTimeMs = stats.AverageActivationTimeMs;
                CacheHitRate = (int)cacheStats.HitRate;
                
                PerformanceStatus = stats.AverageActivationTimeMs switch
                {
                    < 50 => "Excellent",
                    < 100 => "Good",
                    < 200 => "Fair",
                    _ => "Slow"
                };
                
                OnPropertyChanged(nameof(AverageActivationTimeMs));
                OnPropertyChanged(nameof(CacheHitRate));
                OnPropertyChanged(nameof(PerformanceStatus));
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    "Error updating performance metrics", 
                    ex, 
                    "CharacterCollectionViewModel");
            }
        }

        #endregion

        #region Private Methods - Data Loading

        private async Task LoadCharactersAsync()
        {
            if (!await _updateSemaphore.WaitAsync(100))
                return; // Another update in progress
            
            try
            {
                var orderedCharacters = await _orderingService.GetOrderedCharactersAsync();
                
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Unsubscribe from old view models
                    foreach (var vm in Characters)
                    {
                        vm.OnActivateRequested -= OnCharacterActivateRequested;
                        vm.Dispose();
                    }
                    
                    Characters.Clear();
                    
                    // Create new view models
                    for (int i = 0; i < orderedCharacters.Count; i++)
                    {
                        var vm = new CharacterItemViewModel(orderedCharacters[i], i)
                        {
                            CanMoveDown = (i < orderedCharacters.Count - 1)
                        };
                        
                        vm.OnActivateRequested += OnCharacterActivateRequested;
                        Characters.Add(vm);
                    }
                    
                    OnPropertyChanged(nameof(CharacterCount));
                    OnPropertyChanged(nameof(HasRunningCharacters));
                });
                
                await _loggingService.LogDebugAsync(
                    $"Loaded {orderedCharacters.Count} characters", 
                    "CharacterCollectionViewModel");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync(
                    "Failed to load characters", 
                    ex, 
                    "CharacterCollectionViewModel");
            }
            finally
            {
                _updateSemaphore.Release();
            }
        }

        #endregion

        #region Event Handlers

        private void SubscribeToServices()
        {
            // Subscribe to ordering service updates
            _orderingService.CharacterCacheUpdated += OnOrderingServiceUpdated;
            
            // Subscribe to monitor service events
            _monitorService.CharacterDetected += OnCharacterDetected;
            _monitorService.CharacterUpdated += OnCharacterUpdated;
            _monitorService.CharacterRemoved += OnCharacterRemoved;
            
            // Subscribe to activation service for status updates
            _activationService.CharacterActivated += OnCharacterActivated;
            
            // Start monitoring
            _monitorService.StartMonitoring();
        }

        private void OnOrderingServiceUpdated(object? sender, CharacterCacheUpdatedEventArgs e)
        {
            _ = LoadCharactersAsync();
        }

        private void OnCharacterDetected(object? sender, PlayOnlineCharacterEventArgs e)
        {
            _ = LoadCharactersAsync();
        }

        private void OnCharacterUpdated(object? sender, PlayOnlineCharacterEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var vm = Characters.FirstOrDefault(c => c.ProcessId == e.Character.ProcessId);
                if (vm != null)
                {
                    // **FIX**: Update the underlying character data with new information
                    // Window title IS the character name - both get updated together
                    vm.Character.CharacterName = e.Character.CharacterName;
                    vm.Character.WindowTitle = e.Character.WindowTitle;
                    vm.Character.ServerName = e.Character.ServerName;
                    vm.Character.WindowHandle = e.Character.WindowHandle;
                    vm.Character.LastSeen = e.Character.LastSeen;
                    
                    // Refresh the status display
                    vm.RefreshStatusDisplay();
                }
            });
        }

        private void OnCharacterRemoved(object? sender, PlayOnlineCharacterEventArgs e)
        {
            _ = LoadCharactersAsync();
        }

        private void OnCharacterActivated(object? sender, HotkeyActivationResult e)
        {
            if (!e.Success || e.Character == null)
                return;
            
            Application.Current.Dispatcher.Invoke(() =>
            {
                var vm = Characters.FirstOrDefault(c => c.ProcessId == e.Character.ProcessId);
                if (vm != null)
                {
                    UpdateLastActivatedCharacter(vm);
                }
            });
        }

        private void OnCharacterActivateRequested(object? sender, EventArgs e)
        {
            if (sender is CharacterItemViewModel vm)
            {
                _ = ActivateCharacterAsync(vm);
            }
        }

        private void OnCharactersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(CharacterCount));
            OnPropertyChanged(nameof(HasRunningCharacters));
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;
            
            // Stop services
            _monitorService.StopMonitoring();
            _statusRefreshTimer?.Stop();
            
            // Unsubscribe from events
            _orderingService.CharacterCacheUpdated -= OnOrderingServiceUpdated;
            _monitorService.CharacterDetected -= OnCharacterDetected;
            _monitorService.CharacterUpdated -= OnCharacterUpdated;
            _monitorService.CharacterRemoved -= OnCharacterRemoved;
            _activationService.CharacterActivated -= OnCharacterActivated;
            
            // Dispose view models
            foreach (var vm in Characters)
            {
                vm.OnActivateRequested -= OnCharacterActivateRequested;
                vm.Dispose();
            }
            
            Characters.CollectionChanged -= OnCharactersCollectionChanged;
            
            // Dispose resources
            _updateSemaphore?.Dispose();
            _cts?.Cancel();
            _cts?.Dispose();
            
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}