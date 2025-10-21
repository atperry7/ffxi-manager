using FFXIManager.Infrastructure;
using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.ViewModels.Base;
using System.Collections.ObjectModel;
using FFXIManager.Models.Settings;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;

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

        // Duration update timer
        private readonly DispatcherTimer _durationUpdateTimer;

        private readonly IServiceProvider _serviceProvider;

        public AutoLoginQueueViewModel(
            IAutoLoginQueueService queueService,
            IPlayOnlineMemberAccountService accountService,
            IProfileService profileService,
            IStatusMessageService statusService,
            ILoggingService loggingService,
            IDialogService dialogService,
            IUiDispatcher uiDispatcher,
            IServiceProvider serviceProvider)
        {
            _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
            _accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

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
        /// Step-level performance insights aggregated from recent runs.
        /// </summary>
        public IEnumerable<StepPerformanceEntry> StepInsights
        {
            get
            {
                var stats = _queueService.GetStatistics();
                return stats.StepPerformance ?? Enumerable.Empty<StepPerformanceEntry>();
            }
        }

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
        /// Whether the Start button should be visible (queue is idle)
        /// </summary>
        public bool CanShowStartButton => ExecutionState == QueueExecutionState.Idle;

        /// <summary>
        /// Whether the Pause button should be visible (queue is processing or transitioning)
        /// </summary>
        public bool CanShowPauseButton => ExecutionState is QueueExecutionState.Processing or QueueExecutionState.Transitioning;

        /// <summary>
        /// Whether the Resume button should be visible (queue is paused)
        /// </summary>
        public bool CanShowResumeButton => ExecutionState == QueueExecutionState.Paused;

        /// <summary>
        /// Whether the Stop button should be visible (queue is executing)
        /// </summary>
        public bool CanShowStopButton => ExecutionState is QueueExecutionState.Starting or QueueExecutionState.Processing
                                         or QueueExecutionState.Transitioning or QueueExecutionState.Paused;

        /// <summary>
        /// Whether the Reset button should be visible (queue is idle)
        /// </summary>
        public bool CanShowResetButton => ExecutionState == QueueExecutionState.Idle;

        /// <summary>
        /// Whether progress bars should be visible (queue is executing or has results to show)
        /// </summary>
        public bool ShowProgressBars => ExecutionState is QueueExecutionState.Starting or QueueExecutionState.Processing
                                        or QueueExecutionState.Transitioning or QueueExecutionState.Stopping
                                        || (ProcessedItems > 0 && TotalQueueItems > 0);

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
        /// Number of cancelled/skipped items
        /// </summary>
        public int CancelledItems => _queueService.CancelledItems;

        /// <summary>
        /// Total number of processed items (completed + failed + cancelled)
        /// </summary>
        public int ProcessedItems => _queueService.ProcessedItems;

        /// <summary>
        /// Overall queue progress percentage
        /// </summary>
        public int OverallProgress => _queueService.OverallProgress;

        /// <summary>
        /// Percentage of completed items for progress bar visualization
        /// </summary>
        public double CompletedPercentage => TotalQueueItems > 0 ? (double)CompletedItems / TotalQueueItems * 100 : 0;

        /// <summary>
        /// Percentage of failed items for progress bar visualization
        /// </summary>
        public double FailedPercentage => TotalQueueItems > 0 ? (double)FailedItems / TotalQueueItems * 100 : 0;

        /// <summary>
        /// Percentage of cancelled/skipped items for progress bar visualization
        /// </summary>
        public double CancelledPercentage => TotalQueueItems > 0 ? (double)CancelledItems / TotalQueueItems * 100 : 0;

        /// <summary>
        /// Combined percentage of failed and cancelled items for stacked progress bar
        /// </summary>
        public double FailedAndCancelledPercentage => FailedPercentage + CancelledPercentage;

        /// <summary>
        /// Combined percentage of completed and cancelled items (non-failed) for stacked progress bar
        /// </summary>
        public double CompletedAndCancelledPercentage => CompletedPercentage + CancelledPercentage;

        /// <summary>
        /// Total percentage of all processed items for stacked progress bar
        /// </summary>
        public double ProcessedPercentage => TotalQueueItems > 0 ? (double)ProcessedItems / TotalQueueItems * 100 : 0;

        /// <summary>
        /// Gets user-friendly processing status text with context and timing
        /// </summary>
        private string GetProcessingStatusText()
        {
            if (TotalQueueItems == 0) return "Processing...";

            // Calculate current position: processed items + 1 for active item
            // But only if there's actually an active item (not all items are already processed)
            var currentPosition = ProcessedItems < TotalQueueItems ? ProcessedItems + 1 : ProcessedItems;

            // Add contextual information based on current activity
            var baseStatus = $"Logging In Account {currentPosition}/{TotalQueueItems}";

            // Add timing context for longer operations
            if (CurrentItem != null && CurrentItem.StartTime.HasValue)
            {
                var duration = DateTime.UtcNow - CurrentItem.StartTime.Value;
                if (duration.TotalSeconds > 30)
                {
                    return $"{baseStatus} (This may take 1-2 minutes)";
                }
            }

            return baseStatus;
        }

        /// <summary>
        /// Current item being processed
        /// </summary>
        public AutoLoginQueueItem? CurrentItem => _queueService.CurrentItem;

        /// <summary>
        /// Current execution state of the queue
        /// </summary>
        public QueueExecutionState ExecutionState => _queueService.ExecutionState;

        /// <summary>
        /// Message displayed during transitions
        /// </summary>
        public string TransitioningMessage => _queueService.TransitioningMessage;

        /// <summary>
        /// Queue execution status display with user-friendly messaging
        /// </summary>
        public string QueueStatusDisplay
        {
            get
            {
                return ExecutionState switch
                {
                    QueueExecutionState.Idle => "Ready to Start",
                    QueueExecutionState.Starting => "Initializing Auto-Login...",
                    QueueExecutionState.Processing => GetProcessingStatusText(),
                    QueueExecutionState.Transitioning => "Switching Profiles...",
                    QueueExecutionState.Paused => "Paused",
                    QueueExecutionState.Stopping => "Stopping Operations...",
                    QueueExecutionState.Completed => "All Accounts Processed",
                    _ => "Unknown"
                };
            }
        }

        /// <summary>
        /// Queue progress display text with enhanced user-friendly messaging
        /// </summary>
        public string QueueProgressDisplay
        {
            get
            {
                if (TotalQueueItems == 0) return "Add accounts to begin auto-login";

                // Show detailed breakdown during and after execution
                if (ProcessedItems > 0)
                {
                    var parts = new List<string>();

                    if (CompletedItems > 0) parts.Add($"{CompletedItems} completed");
                    if (FailedItems > 0) parts.Add($"{FailedItems} failed");
                    if (CancelledItems > 0) parts.Add($"{CancelledItems} skipped");

                    var breakdown = parts.Count > 0 ? $" ({string.Join(", ", parts)})" : "";

                    return $"{ProcessedItems}/{TotalQueueItems} accounts processed{breakdown}";
                }

                // Pre-execution state
                return $"{TotalQueueItems} account{(TotalQueueItems == 1 ? "" : "s")} ready for auto-login";
            }
        }

        /// <summary>
        /// User-friendly message to display when queue is idle (no current item)
        /// </summary>
        public string IdleStateMessage
        {
            get
            {
                // If queue is transitioning, show the transition message
                if (ExecutionState == QueueExecutionState.Transitioning)
                    return "Preparing next account...";

                // If queue is starting, show starting message
                if (ExecutionState == QueueExecutionState.Starting)
                    return "Initializing auto-login system...";

                // If queue is stopping, show stopping message
                if (ExecutionState == QueueExecutionState.Stopping)
                    return "Finishing current operations and stopping...";

                if (TotalQueueItems == 0)
                    return "Welcome to Auto-Login! Add accounts to get started";

                if (ProcessedItems == TotalQueueItems)
                {
                    if (FailedItems == 0 && CancelledItems == 0)
                        return $"🎉 Success! All {CompletedItems} accounts logged in successfully";
                    else
                    {
                        var successRate = CompletedItems > 0 ? $" ({(CompletedItems * 100 / TotalQueueItems)}% success rate)" : "";
                        return $"✅ Auto-login completed{successRate}";
                    }
                }

                if (ProcessedItems > 0)
                    return $"⏸️ Auto-login paused - {ProcessedItems} of {TotalQueueItems} accounts processed";

                return "🚀 Ready to start - Click the play button to begin auto-login";
            }
        }

        /// <summary>
        /// Helpful step message to display when queue is idle with contextual guidance
        /// </summary>
        public string IdleStepMessage
        {
            get
            {
                if (TotalQueueItems == 0)
                    return "💡 Tip: Use the PlayOnline Member Accounts section above to add accounts to the queue";

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
                    var icon = lastProcessedItem.Status == AutoLoginQueueStatus.Completed ? "✅" : "❌";
                    var status = lastProcessedItem.Status == AutoLoginQueueStatus.Completed ? "completed" : "failed";
                    var duration = lastProcessedItem.DurationDisplay;
                    return $"{icon} Last {status}: {lastProcessedItem.DisplayName} (took {duration})";
                }

                // Check if there are failed items that could be retried
                var failedCount = QueueItems.Count(x => x.Status == AutoLoginQueueStatus.Failed);
                if (failedCount > 0)
                {
                    return $"💡 Tip: {failedCount} failed account{(failedCount == 1 ? "" : "s")} can be retried - right-click to retry individual accounts";
                }

                return "All accounts are ready for auto-login";
            }
        }

        // IdleProgressValue removed. Bind directly to OverallProgress.

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
        public ICommand OpenWorkflowEditorCommand { get; private set; } = null!;

        // Parameter-based commands
        public ICommand RemoveItemParameterCommand { get; private set; } = null!;
        public ICommand RetryItemParameterCommand { get; private set; } = null!;
        public ICommand LoginNowParameterCommand { get; private set; } = null!;

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
                () => TotalQueueItems > 0 && ExecutionState == QueueExecutionState.Idle);

            StopQueueCommand = new RelayCommand(
                async () => await StopQueueAsync(),
                () => ExecutionState is QueueExecutionState.Starting or QueueExecutionState.Processing
                      or QueueExecutionState.Transitioning or QueueExecutionState.Paused);

            PauseQueueCommand = new RelayCommand(
                async () => await PauseQueueAsync(),
                () => ExecutionState is QueueExecutionState.Processing or QueueExecutionState.Transitioning);

            ResumeQueueCommand = new RelayCommand(
                async () => await ResumeQueueAsync(),
                () => ExecutionState == QueueExecutionState.Paused);

            SkipCurrentCommand = new RelayCommand(
                async () => await SkipCurrentItemAsync(),
                () => CurrentItem != null &&
                      (ExecutionState == QueueExecutionState.Processing || ExecutionState == QueueExecutionState.Paused));

            RetryFailedCommand = new RelayCommand(
                async () => await RetryFailedItemAsync(),
                () => SelectedQueueItem?.Status == AutoLoginQueueStatus.Failed);

            ResetQueueCommand = new RelayCommand(
                async () => await ResetQueueAsync(),
                () => TotalQueueItems > 0 && ExecutionState == QueueExecutionState.Idle);

            RefreshAccountsCommand = new RelayCommand(
                async () => await RefreshAvailableAccountsAsync());

            OpenWorkflowEditorCommand = new RelayCommand(
                () => OpenWorkflowEditor());

            // Parameter-based commands
            RemoveItemParameterCommand = new RelayCommandWithParameter<AutoLoginQueueItem>(
                async item => await RemoveItemAsync(item));

            RetryItemParameterCommand = new RelayCommandWithParameter<AutoLoginQueueItem>(
                async item => await RetryItemAsync(item));

            LoginNowParameterCommand = new RelayCommandWithParameter<AutoLoginQueueItem>(
                async item => await LoginNowAsync(item));
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
                // Auto-reset completed items for fresh run while preserving the queue configuration
                if (QueueItems.Any(x => x.Status == AutoLoginQueueStatus.Completed || x.Status == AutoLoginQueueStatus.Failed))
                {
                    await _loggingService.LogInfoAsync("Resetting completed/failed items for fresh queue run");
                    await _queueService.ResetQueueAsync();
                }

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
                    "Are you sure you want to stop the queue execution?\n\nThis will reset all pending/failed items back to pending state for a clean restart.");

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

        private async Task LoginNowAsync(AutoLoginQueueItem item)
        {
            if (item == null)
                return;

            var account = item.Account;
            var profile = item.Profile;

            if (account == null || profile == null)
            {
                _statusService.SetTemporaryMessage("Invalid queue item selected", TimeSpan.FromSeconds(3));
                return;
            }

            if (!account.HasStoredPassword)
            {
                _statusService.SetTemporaryMessage($"Cannot login {account.DisplayName} - no password stored. Edit the account to set a password first.", TimeSpan.FromSeconds(5));
                await _loggingService.LogWarningAsync($"Attempted immediate login for {account.DisplayName} without stored password");
                return;
            }

            if (_queueService.IsExecuting)
            {
                _statusService.SetTemporaryMessage("Cannot start login now - queue is already executing", TimeSpan.FromSeconds(3));
                return;
            }

            try
            {
                await _queueService.StartImmediateLoginAsync(item, _cancellationTokenSource.Token);
                _statusService.SetTemporaryMessage($"Starting immediate login for {account.DisplayName}", TimeSpan.FromSeconds(3));
                await _loggingService.LogInfoAsync($"Started immediate login for {account.DisplayName} from profile {profile.Name} via queue view");
            }
            catch (Exception ex)
            {
                _statusService.SetTemporaryMessage($"Failed to start immediate login for {account.DisplayName}", TimeSpan.FromSeconds(3));
                await _loggingService.LogErrorAsync($"Error starting immediate login for {account.DisplayName}", ex);
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
                    UpdateQueueProperties(); // Ensure all progress properties are updated
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

                // Allow user to review completion results - no auto-reset
                if (e.Reason == QueueStopReason.Completed)
                {
                    await _loggingService.LogInfoAsync("Queue completed successfully - results preserved for user review");
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
                UpdateQueueProperties();
                _statusService.SetMessage($"Starting login: {e.Item.DisplayName}");
            });
        }

        private void OnItemCompleted(object? sender, AutoLoginQueueItemEventArgs e)
        {
            _uiDispatcher.InvokeAsync(() =>
            {
                UpdateQueueProperties();
                UpdateCommandStates();
                _statusService.SetTemporaryMessage($"Completed: {e.Item.DisplayName}", TimeSpan.FromSeconds(3));
            });
        }

        private void OnItemFailed(object? sender, AutoLoginQueueItemEventArgs e)
        {
            _uiDispatcher.InvokeAsync(() =>
            {
                UpdateQueueProperties();
                UpdateCommandStates();

                // Different messages for different failure types
                var message = e.Item.Status == AutoLoginQueueStatus.Cancelled
                    ? $"Skipped: {e.Item.DisplayName}"
                    : $"Failed: {e.Item.DisplayName} - {e.Message}";

                _statusService.SetTemporaryMessage(message, TimeSpan.FromSeconds(3));
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

        private void OpenWorkflowEditor()
        {
            try
            {
                var window = _serviceProvider.GetService(typeof(FFXIManager.Views.WorkflowEditorWindow)) as System.Windows.Window;
                if (window != null)
                {
                    window.Owner = System.Windows.Application.Current?.MainWindow;
                    window.Show();
                }
                else
                {
                    _ = _loggingService.LogWarningAsync("WorkflowEditor window could not be created");
                }
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("Failed to open Workflow Editor", ex);
            }
        }

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
            OnPropertyChanged(nameof(ExecutionState));
            OnPropertyChanged(nameof(TransitioningMessage));
            OnPropertyChanged(nameof(CanShowStartButton));
            OnPropertyChanged(nameof(CanShowPauseButton));
            OnPropertyChanged(nameof(CanShowResumeButton));
            OnPropertyChanged(nameof(CanShowStopButton));
            OnPropertyChanged(nameof(CanShowResetButton));
            OnPropertyChanged(nameof(ShowProgressBars));
            OnPropertyChanged(nameof(TotalQueueItems));
            OnPropertyChanged(nameof(CompletedItems));
            OnPropertyChanged(nameof(FailedItems));
            OnPropertyChanged(nameof(CancelledItems));
            OnPropertyChanged(nameof(ProcessedItems));
            OnPropertyChanged(nameof(OverallProgress));
            OnPropertyChanged(nameof(CompletedPercentage));
            OnPropertyChanged(nameof(FailedPercentage));
            OnPropertyChanged(nameof(CancelledPercentage));
            OnPropertyChanged(nameof(FailedAndCancelledPercentage));
            OnPropertyChanged(nameof(CompletedAndCancelledPercentage));
            OnPropertyChanged(nameof(ProcessedPercentage));
            OnPropertyChanged(nameof(CurrentItem));
            OnPropertyChanged(nameof(QueueStatusDisplay));
            OnPropertyChanged(nameof(QueueProgressDisplay));
            OnPropertyChanged(nameof(IdleStateMessage));

            // Manage duration update timer based on execution state
            var isExecuting = ExecutionState is QueueExecutionState.Starting or QueueExecutionState.Processing
                             or QueueExecutionState.Transitioning or QueueExecutionState.Stopping;
            if (isExecuting && !_durationUpdateTimer.IsEnabled)
            {
                _durationUpdateTimer.Start();
            }
            else if (!isExecuting && _durationUpdateTimer.IsEnabled)
            {
                _durationUpdateTimer.Stop();
            }
            OnPropertyChanged(nameof(IdleStepMessage));
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
            (ResetQueueCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            // Reset queue items to pending for clean application restart
            try
            {
                if (QueueItems.Any(x => x.Status != AutoLoginQueueStatus.Pending))
                {
                    _queueService.ResetQueueAsync().Wait(TimeSpan.FromSeconds(2)); // Brief wait for clean shutdown
                    _loggingService.LogInfoAsync("Reset queue items to pending on application closure").Wait(TimeSpan.FromSeconds(1));
                }
            }
            catch
            {
                // Ignore exceptions during disposal
            }

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



