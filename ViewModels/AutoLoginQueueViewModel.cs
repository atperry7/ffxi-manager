using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using FFXIManager.Infrastructure;
using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.ViewModels.Base;

namespace FFXIManager.ViewModels
{
    /// <summary>
    /// ViewModel for managing the auto-login queue
    /// </summary>
    public class AutoLoginQueueViewModel : ViewModelBase, IDisposable
    {
        private readonly IAutoLoginQueueService _queueService;
        private readonly IPlayOnlineMemberAccountService _accountService;
        private readonly IProfileService _profileService;
        private readonly IStatusMessageService _statusService;
        private readonly ILoggingService _loggingService;
        private readonly IDialogService _dialogService;
        private readonly IUiDispatcher _uiDispatcher;

        private ProfileInfo? _currentProfile;
        private AutoLoginQueueItem? _selectedQueueItem;
        private ObservableCollection<PlayOnlineMemberAccount>? _availableAccounts;
        private PlayOnlineMemberAccount? _selectedAccount;
        private bool _isLoading;
        private bool _disposed;
        private CancellationTokenSource _cancellationTokenSource = new();

        // Profile switching tracking
        private string? _lastActiveProfileName;
        private bool _isProfileSwitching;

        // Duration update timer
        private readonly DispatcherTimer _durationUpdateTimer;

        public AutoLoginQueueViewModel(
            IAutoLoginQueueService queueService,
            IPlayOnlineMemberAccountService accountService,
            IProfileService profileService,
            IStatusMessageService statusService,
            ILoggingService loggingService,
            IDialogService dialogService,
            IUiDispatcher uiDispatcher)
        {
            _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
            _accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));

            AvailableAccounts = new ObservableCollection<PlayOnlineMemberAccount>();

            // Initialize duration update timer
            _durationUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _durationUpdateTimer.Tick += OnDurationUpdateTimer;

            InitializeCommands();
            SubscribeToQueueEvents();

            // Load queue state on startup
            _ = LoadQueueStateAsync();
        }

        #region Properties

        /// <summary>
        /// Queue items from the service
        /// </summary>
        public ObservableCollection<AutoLoginQueueItem> QueueItems => _queueService.QueueItems;

        /// <summary>
        /// Available accounts for selection
        /// </summary>
        public ObservableCollection<PlayOnlineMemberAccount> AvailableAccounts
        {
            get => _availableAccounts ?? new ObservableCollection<PlayOnlineMemberAccount>();
            set => SetProperty(ref _availableAccounts, value);
        }

        /// <summary>
        /// Currently selected profile
        /// </summary>
        public ProfileInfo? CurrentProfile
        {
            get => _currentProfile;
            set
            {
                if (SetProperty(ref _currentProfile, value))
                {
                    _ = RefreshAvailableAccountsAsync();
                    OnPropertyChanged(nameof(HasProfileSelected));
                    UpdateCommandStates();
                }
            }
        }

        /// <summary>
        /// Selected account for adding to queue
        /// </summary>
        public PlayOnlineMemberAccount? SelectedAccount
        {
            get => _selectedAccount;
            set
            {
                if (SetProperty(ref _selectedAccount, value))
                {
                    UpdateCommandStates();
                }
            }
        }

        /// <summary>
        /// Selected queue item
        /// </summary>
        public AutoLoginQueueItem? SelectedQueueItem
        {
            get => _selectedQueueItem;
            set
            {
                if (SetProperty(ref _selectedQueueItem, value))
                {
                    UpdateCommandStates();
                }
            }
        }

        /// <summary>
        /// Whether data is being loaded
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        /// <summary>
        /// Whether a profile is selected
        /// </summary>
        public bool HasProfileSelected => CurrentProfile != null && !CurrentProfile.IsSystemFile;

        /// <summary>
        /// Whether the queue is currently executing
        /// </summary>
        public bool IsQueueExecuting => _queueService.IsExecuting;

        /// <summary>
        /// Whether the queue is paused
        /// </summary>
        public bool IsQueuePaused => _queueService.IsPaused;

        /// <summary>
        /// Total number of items in queue
        /// </summary>
        public int TotalQueueItems => _queueService.TotalItems;

        /// <summary>
        /// Number of completed items
        /// </summary>
        public int CompletedItems => _queueService.CompletedItems;

        /// <summary>
        /// Number of failed items
        /// </summary>
        public int FailedItems => _queueService.FailedItems;

        /// <summary>
        /// Overall queue progress percentage
        /// </summary>
        public int OverallProgress => _queueService.OverallProgress;

        /// <summary>
        /// Current item being processed
        /// </summary>
        public AutoLoginQueueItem? CurrentItem => _queueService.CurrentItem;

        /// <summary>
        /// Queue execution status display
        /// </summary>
        public string QueueStatusDisplay
        {
            get
            {
                if (!IsQueueExecuting) return "Ready";
                if (IsQueuePaused) return "Paused";
                return $"Running ({CompletedItems + FailedItems + 1}/{TotalQueueItems})";
            }
        }

        /// <summary>
        /// Queue progress display text
        /// </summary>
        public string QueueProgressDisplay
        {
            get
            {
                if (TotalQueueItems == 0) return "No items in queue";
                var processedItems = CompletedItems + FailedItems;
                return $"{processedItems}/{TotalQueueItems} items processed ({OverallProgress}%)";
            }
        }

        /// <summary>
        /// Whether we're currently switching between profiles
        /// </summary>
        public bool IsProfileSwitching
        {
            get => _isProfileSwitching;
            private set => SetProperty(ref _isProfileSwitching, value);
        }

        /// <summary>
        /// Name of the previously active profile (for transition display)
        /// </summary>
        public string? LastActiveProfileName
        {
            get => _lastActiveProfileName;
            private set => SetProperty(ref _lastActiveProfileName, value);
        }

        /// <summary>
        /// Profile transition message for UI display
        /// </summary>
        public string ProfileTransitionMessage
        {
            get
            {
                if (CurrentItem?.ProfileName != null && LastActiveProfileName != null &&
                    CurrentItem.ProfileName != LastActiveProfileName)
                {
                    return $"Switched from profile '{LastActiveProfileName}' to '{CurrentItem.ProfileName}'";
                }
                return string.Empty;
            }
        }

        /// <summary>
        /// Whether to show the profile transition indicator
        /// </summary>
        public bool ShowProfileTransition => !string.IsNullOrEmpty(ProfileTransitionMessage) && IsProfileSwitching;

        /// <summary>
        /// Message to display when queue is idle (no current item)
        /// </summary>
        public string IdleStateMessage
        {
            get
            {
                if (TotalQueueItems == 0)
                    return "Ready to start - Add accounts to the queue";

                if (CompletedItems == TotalQueueItems)
                    return $"Queue completed - {CompletedItems} accounts processed successfully";

                if (FailedItems > 0 && CompletedItems + FailedItems == TotalQueueItems)
                    return $"Queue finished - {CompletedItems} completed, {FailedItems} failed";

                if (CompletedItems > 0 || FailedItems > 0)
                    return $"Queue paused - {CompletedItems + FailedItems}/{TotalQueueItems} items processed";

                return "Queue ready - Click Start to begin auto-login";
            }
        }

        /// <summary>
        /// Step message to display when queue is idle
        /// </summary>
        public string IdleStepMessage
        {
            get
            {
                if (TotalQueueItems == 0)
                    return "Use the PlayOnline Member Accounts section to add accounts to the queue";

                var lastCompletedItem = QueueItems
                    .Where(x => x.Status == AutoLoginQueueStatus.Completed)
                    .OrderByDescending(x => x.EndTime)
                    .FirstOrDefault();

                var lastFailedItem = QueueItems
                    .Where(x => x.Status == AutoLoginQueueStatus.Failed)
                    .OrderByDescending(x => x.EndTime)
                    .FirstOrDefault();

                // Show the most recent completed or failed item
                var recentItems = new List<AutoLoginQueueItem>();
                if (lastCompletedItem != null) recentItems.Add(lastCompletedItem);
                if (lastFailedItem != null) recentItems.Add(lastFailedItem);

                var lastProcessedItem = recentItems
                    .OrderByDescending(x => x.EndTime ?? DateTime.MinValue)
                    .FirstOrDefault();

                if (lastProcessedItem != null)
                {
                    var status = lastProcessedItem.Status == AutoLoginQueueStatus.Completed ? "completed" : "failed";
                    var duration = lastProcessedItem.DurationDisplay;
                    return $"Last {status}: {lastProcessedItem.DisplayName} ({duration})";
                }

                return "Ready to process queue items";
            }
        }

        /// <summary>
        /// Progress value to display when queue is idle (0-100)
        /// </summary>
        public int IdleProgressValue
        {
            get
            {
                if (TotalQueueItems == 0) return 0;
                return OverallProgress; // Reuse the existing overall progress calculation
            }
        }

        #endregion

        #region Commands

        public ICommand AddAccountToQueueCommand { get; private set; } = null!;
        public ICommand RemoveFromQueueCommand { get; private set; } = null!;
        public ICommand ClearQueueCommand { get; private set; } = null!;
        public ICommand MoveUpCommand { get; private set; } = null!;
        public ICommand MoveDownCommand { get; private set; } = null!;
        public ICommand StartQueueCommand { get; private set; } = null!;
        public ICommand StopQueueCommand { get; private set; } = null!;
        public ICommand PauseQueueCommand { get; private set; } = null!;
        public ICommand ResumeQueueCommand { get; private set; } = null!;
        public ICommand SkipCurrentCommand { get; private set; } = null!;
        public ICommand RetryFailedCommand { get; private set; } = null!;
        public ICommand ResetQueueCommand { get; private set; } = null!;
        public ICommand RefreshAccountsCommand { get; private set; } = null!;

        // Parameter-based commands
        public ICommand RemoveItemParameterCommand { get; private set; } = null!;
        public ICommand RetryItemParameterCommand { get; private set; } = null!;

        private void InitializeCommands()
        {
            AddAccountToQueueCommand = new RelayCommand(
                async () => await AddAccountToQueueAsync(),
                () => HasProfileSelected && SelectedAccount != null && !IsQueueExecuting);

            RemoveFromQueueCommand = new RelayCommand(
                async () => await RemoveFromQueueAsync(),
                () => SelectedQueueItem != null && !IsQueueExecuting);

            ClearQueueCommand = new RelayCommand(
                async () => await ClearQueueAsync(),
                () => TotalQueueItems > 0 && !IsQueueExecuting);

            MoveUpCommand = new RelayCommand(
                async () => await MoveItemUpAsync(),
                () => SelectedQueueItem != null && SelectedQueueItem.Position > 1 && !IsQueueExecuting);

            MoveDownCommand = new RelayCommand(
                async () => await MoveItemDownAsync(),
                () => SelectedQueueItem != null && SelectedQueueItem.Position < TotalQueueItems && !IsQueueExecuting);

            StartQueueCommand = new RelayCommand(
                async () => await StartQueueAsync(),
                () => TotalQueueItems > 0 && !IsQueueExecuting);

            StopQueueCommand = new RelayCommand(
                async () => await StopQueueAsync(),
                () => IsQueueExecuting);

            PauseQueueCommand = new RelayCommand(
                async () => await PauseQueueAsync(),
                () => IsQueueExecuting && !IsQueuePaused);

            ResumeQueueCommand = new RelayCommand(
                async () => await ResumeQueueAsync(),
                () => IsQueueExecuting && IsQueuePaused);

            SkipCurrentCommand = new RelayCommand(
                async () => await SkipCurrentItemAsync(),
                () => CurrentItem != null);

            RetryFailedCommand = new RelayCommand(
                async () => await RetryFailedItemAsync(),
                () => SelectedQueueItem?.Status == AutoLoginQueueStatus.Failed);

            ResetQueueCommand = new RelayCommand(
                async () => await ResetQueueAsync(),
                () => TotalQueueItems > 0 && !IsQueueExecuting);

            RefreshAccountsCommand = new RelayCommand(
                async () => await RefreshAvailableAccountsAsync());

            // Parameter-based commands
            RemoveItemParameterCommand = new RelayCommandWithParameter<AutoLoginQueueItem>(
                async item => await RemoveItemAsync(item));

            RetryItemParameterCommand = new RelayCommandWithParameter<AutoLoginQueueItem>(
                async item => await RetryItemAsync(item));
        }

        #endregion

        #region Command Implementations

        private async Task AddAccountToQueueAsync()
        {
            if (SelectedAccount == null || CurrentProfile == null) return;

            try
            {
                // Check if account is already in queue
                var existingItem = QueueItems.FirstOrDefault(x =>
                    x.Account.Id == SelectedAccount.Id &&
                    x.Profile.FilePath == CurrentProfile.FilePath);

                if (existingItem != null)
                {
                    _statusService.SetTemporaryMessage("Account is already in the queue", TimeSpan.FromSeconds(3));
                    return;
                }

                var queueItem = await _queueService.AddToQueueAsync(SelectedAccount, CurrentProfile);
                _statusService.SetTemporaryMessage($"Added {SelectedAccount.DisplayName} to queue", TimeSpan.FromSeconds(3));

                // Clear selection
                SelectedAccount = null;

                UpdateCommandStates();
                UpdateQueueProperties();
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error adding account to queue", ex);
                _statusService.SetTemporaryMessage("Failed to add account to queue", TimeSpan.FromSeconds(3));
            }
        }

        private async Task RemoveFromQueueAsync()
        {
            if (SelectedQueueItem == null) return;
            await RemoveItemAsync(SelectedQueueItem);
        }

        private async Task RemoveItemAsync(AutoLoginQueueItem item)
        {
            if (item == null) return;

            try
            {
                var result = await _dialogService.ShowConfirmationDialogAsync(
                    "Remove Item",
                    $"Are you sure you want to remove '{item.DisplayName}' from the queue?");

                if (result)
                {
                    await _queueService.RemoveFromQueueAsync(item);
                    _statusService.SetTemporaryMessage($"Removed {item.DisplayName} from queue", TimeSpan.FromSeconds(3));

                    UpdateCommandStates();
                    UpdateQueueProperties();
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error removing item from queue", ex);
                _statusService.SetTemporaryMessage("Failed to remove item from queue", TimeSpan.FromSeconds(3));
            }
        }

        private async Task ClearQueueAsync()
        {
            try
            {
                var result = await _dialogService.ShowConfirmationDialogAsync(
                    "Clear Queue",
                    $"Are you sure you want to clear all {TotalQueueItems} items from the queue?");

                if (result)
                {
                    await _queueService.ClearQueueAsync();
                    _statusService.SetTemporaryMessage("Queue cleared", TimeSpan.FromSeconds(3));

                    UpdateCommandStates();
                    UpdateQueueProperties();
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error clearing queue", ex);
                _statusService.SetTemporaryMessage("Failed to clear queue", TimeSpan.FromSeconds(3));
            }
        }

        private async Task MoveItemUpAsync()
        {
            if (SelectedQueueItem == null) return;

            try
            {
                await _queueService.MoveItemAsync(SelectedQueueItem, SelectedQueueItem.Position - 1);
                UpdateCommandStates();
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error moving item up", ex);
                _statusService.SetTemporaryMessage("Failed to move item", TimeSpan.FromSeconds(3));
            }
        }

        private async Task MoveItemDownAsync()
        {
            if (SelectedQueueItem == null) return;

            try
            {
                await _queueService.MoveItemAsync(SelectedQueueItem, SelectedQueueItem.Position + 1);
                UpdateCommandStates();
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error moving item down", ex);
                _statusService.SetTemporaryMessage("Failed to move item", TimeSpan.FromSeconds(3));
            }
        }

        private async Task StartQueueAsync()
        {
            try
            {
                _statusService.SetMessage("Starting auto-login queue...");
                await _queueService.StartQueueAsync(_cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error starting queue", ex);
                _statusService.SetTemporaryMessage($"Failed to start queue: {ex.Message}", TimeSpan.FromSeconds(5));
            }
        }

        private async Task StopQueueAsync()
        {
            try
            {
                var result = await _dialogService.ShowConfirmationDialogAsync(
                    "Stop Queue",
                    "Are you sure you want to stop the queue execution?");

                if (result)
                {
                    _statusService.SetMessage("Stopping auto-login queue...");
                    await _queueService.StopQueueAsync();
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error stopping queue", ex);
                _statusService.SetTemporaryMessage("Failed to stop queue", TimeSpan.FromSeconds(3));
            }
        }

        private async Task PauseQueueAsync()
        {
            try
            {
                await _queueService.PauseQueueAsync();
                _statusService.SetTemporaryMessage("Queue paused", TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error pausing queue", ex);
                _statusService.SetTemporaryMessage("Failed to pause queue", TimeSpan.FromSeconds(3));
            }
        }

        private async Task ResumeQueueAsync()
        {
            try
            {
                await _queueService.ResumeQueueAsync();
                _statusService.SetTemporaryMessage("Queue resumed", TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error resuming queue", ex);
                _statusService.SetTemporaryMessage("Failed to resume queue", TimeSpan.FromSeconds(3));
            }
        }

        private async Task SkipCurrentItemAsync()
        {
            try
            {
                var result = await _dialogService.ShowConfirmationDialogAsync(
                    "Skip Item",
                    $"Are you sure you want to skip the current item '{CurrentItem?.DisplayName}'?");

                if (result)
                {
                    await _queueService.SkipCurrentItemAsync();
                    _statusService.SetTemporaryMessage("Current item skipped", TimeSpan.FromSeconds(3));
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error skipping current item", ex);
                _statusService.SetTemporaryMessage("Failed to skip item", TimeSpan.FromSeconds(3));
            }
        }

        private async Task RetryFailedItemAsync()
        {
            if (SelectedQueueItem?.Status != AutoLoginQueueStatus.Failed) return;
            await RetryItemAsync(SelectedQueueItem);
        }

        private async Task RetryItemAsync(AutoLoginQueueItem item)
        {
            if (item?.Status != AutoLoginQueueStatus.Failed) return;

            try
            {
                await _queueService.RetryItemAsync(item);
                _statusService.SetTemporaryMessage($"Reset {item.DisplayName} for retry", TimeSpan.FromSeconds(3));
                UpdateCommandStates();
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error retrying item", ex);
                _statusService.SetTemporaryMessage("Failed to retry item", TimeSpan.FromSeconds(3));
            }
        }

        private async Task ResetQueueAsync()
        {
            if (IsQueueExecuting)
            {
                _statusService.SetTemporaryMessage("Cannot reset queue while executing", TimeSpan.FromSeconds(3));
                return;
            }

            try
            {
                var result = await _dialogService.ShowConfirmationDialogAsync(
                    "Reset Queue",
                    "Are you sure you want to reset all queue items back to pending status?");

                if (result)
                {
                    await _queueService.ResetQueueAsync();
                    _statusService.SetTemporaryMessage("Queue reset successfully", TimeSpan.FromSeconds(3));
                    UpdateCommandStates();
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error resetting queue", ex);
                _statusService.SetTemporaryMessage("Failed to reset queue", TimeSpan.FromSeconds(3));
            }
        }

        private async Task RefreshAvailableAccountsAsync()
        {
            if (CurrentProfile == null || CurrentProfile.IsSystemFile) return;

            IsLoading = true;
            try
            {
                var accounts = await _accountService.GetAccountsForProfileAsync(CurrentProfile.FilePath);

                await _uiDispatcher.InvokeAsync(() =>
                {
                    AvailableAccounts.Clear();
                    foreach (var account in accounts.OrderBy(a => a.POLMemberSlot))
                    {
                        AvailableAccounts.Add(account);
                    }
                });

                await _loggingService.LogDebugAsync($"Loaded {accounts.Count} accounts for profile {CurrentProfile.Name}");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error loading accounts", ex);
                _statusService.SetTemporaryMessage("Failed to load accounts", TimeSpan.FromSeconds(3));
            }
            finally
            {
                IsLoading = false;
            }
        }

        #endregion

        #region Event Handlers

        private void SubscribeToQueueEvents()
        {
            _queueService.QueueStarted += OnQueueStarted;
            _queueService.QueueStopped += OnQueueStopped;
            _queueService.QueuePaused += OnQueuePaused;
            _queueService.QueueResumed += OnQueueResumed;
            _queueService.ItemStarted += OnItemStarted;
            _queueService.ItemCompleted += OnItemCompleted;
            _queueService.ItemFailed += OnItemFailed;
            _queueService.ItemProgressUpdated += OnItemProgressUpdated;

            // Subscribe to collection changes to update UI properties
            _queueService.QueueItems.CollectionChanged += OnQueueItemsCollectionChanged;
        }

        private void OnQueueStarted(object? sender, EventArgs e)
        {
            _uiDispatcher.InvokeAsync(() =>
            {
                UpdateQueueProperties();
                UpdateCommandStates();
                _statusService.SetMessage("Auto-login queue started");
            });
        }

        private void OnQueueStopped(object? sender, QueueStoppedEventArgs e)
        {
            _uiDispatcher.InvokeAsync(async () =>
            {
                UpdateQueueProperties();
                UpdateCommandStates();
                _statusService.SetMessage($"Auto-login queue stopped: {e.Message}");

                // Auto-reset queue if it completed successfully
                if (e.Reason == QueueStopReason.Completed)
                {
                    try
                    {
                        await _queueService.ResetQueueAsync();
                        await _loggingService.LogInfoAsync("Queue automatically reset after successful completion");
                    }
                    catch (Exception ex)
                    {
                        await _loggingService.LogErrorAsync("Failed to auto-reset queue after completion", ex);
                    }
                }
            });
        }

        private void OnQueuePaused(object? sender, EventArgs e)
        {
            _uiDispatcher.InvokeAsync(() =>
            {
                UpdateQueueProperties();
                UpdateCommandStates();
            });
        }

        private void OnQueueResumed(object? sender, EventArgs e)
        {
            _uiDispatcher.InvokeAsync(() =>
            {
                UpdateQueueProperties();
                UpdateCommandStates();
            });
        }

        private void OnItemStarted(object? sender, AutoLoginQueueItemEventArgs e)
        {
            _uiDispatcher.InvokeAsync(() =>
            {
                // Check for profile switching
                if (_lastActiveProfileName != null && e.Item.ProfileName != _lastActiveProfileName)
                {
                    IsProfileSwitching = true;
                    _statusService.SetTemporaryMessage($"Switching from profile '{_lastActiveProfileName}' to '{e.Item.ProfileName}'", TimeSpan.FromSeconds(4));

                    // Auto-hide the profile switching indicator after a few seconds
                    _ = Task.Delay(3000).ContinueWith(_ => _uiDispatcher.InvokeAsync(() =>
                    {
                        IsProfileSwitching = false;
                        OnPropertyChanged(nameof(ShowProfileTransition));
                    }));
                }

                LastActiveProfileName = e.Item.ProfileName;
                UpdateQueueProperties();
                _statusService.SetMessage($"Starting login: {e.Item.DisplayName}");
            });
        }

        private void OnItemCompleted(object? sender, AutoLoginQueueItemEventArgs e)
        {
            _uiDispatcher.InvokeAsync(() =>
            {
                UpdateQueueProperties();
                _statusService.SetTemporaryMessage($"Completed: {e.Item.DisplayName}", TimeSpan.FromSeconds(3));
            });
        }

        private void OnItemFailed(object? sender, AutoLoginQueueItemEventArgs e)
        {
            _uiDispatcher.InvokeAsync(() =>
            {
                UpdateQueueProperties();
                _statusService.SetTemporaryMessage($"Failed: {e.Item.DisplayName} - {e.Message}", TimeSpan.FromSeconds(5));
            });
        }

        private void OnItemProgressUpdated(object? sender, AutoLoginQueueItemEventArgs e)
        {
            _uiDispatcher.InvokeAsync(() =>
            {
                UpdateQueueProperties();
            });
        }

        private void OnQueueItemsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            _uiDispatcher.InvokeAsync(() =>
            {
                // Notify all dependent properties when the collection changes
                OnPropertyChanged(nameof(TotalQueueItems));
                OnPropertyChanged(nameof(QueueStatusDisplay));
                OnPropertyChanged(nameof(QueueProgressDisplay));
                UpdateCommandStates();
            });
        }

        private void OnDurationUpdateTimer(object? sender, EventArgs e)
        {
            _uiDispatcher.InvokeAsync(() =>
            {
                // Update duration display only for items that are actively running
                var activeItems = QueueItems.Where(x => x.IsActive && x.StartTime != null && x.EndTime == null).ToList();

                foreach (var item in activeItems)
                {
                    item.RefreshDurationDisplay();
                }

                // If no items are actively running, stop the timer
                if (activeItems.Count == 0 && _durationUpdateTimer.IsEnabled)
                {
                    _durationUpdateTimer.Stop();
                }
            });
        }

        #endregion

        #region Helper Methods

        private async Task LoadQueueStateAsync()
        {
            try
            {
                await _queueService.LoadQueueStateAsync();
                UpdateQueueProperties();
                UpdateCommandStates();
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error loading queue state", ex);
            }
        }

        private void UpdateQueueProperties()
        {
            OnPropertyChanged(nameof(IsQueueExecuting));
            OnPropertyChanged(nameof(IsQueuePaused));
            OnPropertyChanged(nameof(TotalQueueItems));
            OnPropertyChanged(nameof(CompletedItems));
            OnPropertyChanged(nameof(FailedItems));
            OnPropertyChanged(nameof(OverallProgress));
            OnPropertyChanged(nameof(CurrentItem));
            OnPropertyChanged(nameof(QueueStatusDisplay));
            OnPropertyChanged(nameof(QueueProgressDisplay));
            OnPropertyChanged(nameof(ProfileTransitionMessage));
            OnPropertyChanged(nameof(ShowProfileTransition));
            OnPropertyChanged(nameof(IdleStateMessage));

            // Manage duration update timer based on execution state
            if (IsQueueExecuting && !_durationUpdateTimer.IsEnabled)
            {
                _durationUpdateTimer.Start();
            }
            else if (!IsQueueExecuting && _durationUpdateTimer.IsEnabled)
            {
                _durationUpdateTimer.Stop();
            }
            OnPropertyChanged(nameof(IdleStepMessage));
            OnPropertyChanged(nameof(IdleProgressValue));
        }

        private void UpdateCommandStates()
        {
            (AddAccountToQueueCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RemoveFromQueueCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ClearQueueCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (MoveUpCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (MoveDownCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (StartQueueCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (StopQueueCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (PauseQueueCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ResumeQueueCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SkipCurrentCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RetryFailedCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            // Unsubscribe from events
            _queueService.QueueStarted -= OnQueueStarted;
            _queueService.QueueStopped -= OnQueueStopped;
            _queueService.QueuePaused -= OnQueuePaused;
            _queueService.QueueResumed -= OnQueueResumed;
            _queueService.ItemStarted -= OnItemStarted;
            _queueService.ItemCompleted -= OnItemCompleted;
            _queueService.ItemFailed -= OnItemFailed;
            _queueService.ItemProgressUpdated -= OnItemProgressUpdated;
            _queueService.QueueItems.CollectionChanged -= OnQueueItemsCollectionChanged;

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();

            GC.SuppressFinalize(this);
        }

        #endregion
    }
}