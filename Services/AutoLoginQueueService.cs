using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Infrastructure;
using FFXIManager.Models;
using FFXIManager.Models.Settings;

namespace FFXIManager.Services
{
    /// <summary>
    /// Service for managing the auto-login queue with sequential execution and persistence
    /// </summary>
    public class AutoLoginQueueService : IAutoLoginQueueService, IDisposable
    {
        private readonly ISettingsService _settingsService;
        private readonly ILoggingService _loggingService;
        private readonly IProfileService _profileService;
        private readonly IPlayOnlineMemberAccountService _accountService;
        private readonly IUiDispatcher _uiDispatcher;
        private readonly IAutoLoginTaskExecutor _taskExecutor;
        private readonly object _lockObject = new();
        private readonly SemaphoreSlim _executionSemaphore = new(1, 1);

        private CancellationTokenSource? _executionCancellationTokenSource;
        private CancellationTokenSource? _currentItemCancellationTokenSource;
        private Task? _executionTask;
        private bool _disposed;
        private string? _originalProfilePath;

        public AutoLoginQueueService(
            ISettingsService settingsService,
            ILoggingService loggingService,
            IProfileService profileService,
            IPlayOnlineMemberAccountService accountService,
            IUiDispatcher uiDispatcher,
            IAutoLoginTaskExecutor taskExecutor)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));
            _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
            _taskExecutor = taskExecutor ?? throw new ArgumentNullException(nameof(taskExecutor));

            QueueItems = new ObservableCollection<AutoLoginQueueItem>();
            QueueItems.CollectionChanged += (_, _) => UpdateQueuePositions();

            // Subscribe to task executor events for enhanced progress reporting
            SetupTaskExecutorEventHandlers();

            // Load settings
            LoadConfigurationFromSettings();
        }

        #region Properties

        public ObservableCollection<AutoLoginQueueItem> QueueItems { get; }

        public bool IsExecuting { get; private set; }

        public bool IsPaused { get; private set; }

        public AutoLoginQueueItem? CurrentItem { get; private set; }

        public QueueExecutionState ExecutionState { get; private set; } = QueueExecutionState.Idle;

        public string TransitioningMessage { get; private set; } = string.Empty;

        public int TotalItems => QueueItems.Count;

        public int CompletedItems => QueueItems.Count(x => x.Status == AutoLoginQueueStatus.Completed);

        public int FailedItems => QueueItems.Count(x => x.Status == AutoLoginQueueStatus.Failed);

        public int CancelledItems => QueueItems.Count(x => x.Status == AutoLoginQueueStatus.Cancelled);

        public int ProcessedItems => CompletedItems + FailedItems + CancelledItems;

        public int OverallProgress
        {
            get
            {
                if (TotalItems == 0) return 0;
                return (int)((double)ProcessedItems / TotalItems * 100);
            }
        }

        public bool AutoSaveQueueState { get; set; } = true;
        public bool ContinueOnFailure { get; set; } = true;
        public int DelayBetweenItems { get; set; } = 2000;
        public int StepTimeoutSeconds { get; set; } = 30;

        #endregion

        #region State Management

        /// <summary>
        /// Transitions to a new execution state with optional message
        /// </summary>
        private void TransitionToState(QueueExecutionState newState, string message = "")
        {
            var oldState = ExecutionState;
            ExecutionState = newState;
            TransitioningMessage = message;

            // Update legacy properties for backward compatibility
            IsExecuting = newState is QueueExecutionState.Starting or QueueExecutionState.Processing
                or QueueExecutionState.Transitioning or QueueExecutionState.Stopping;
            IsPaused = newState == QueueExecutionState.Paused;

            if (oldState != newState)
            {
                _loggingService.LogInfoAsync($"Queue state transition: {oldState} → {newState}" +
                    (string.IsNullOrEmpty(message) ? "" : $" ({message})"));
            }
        }

        /// <summary>
        /// Validates if a state transition is allowed
        /// </summary>
        private bool CanTransitionTo(QueueExecutionState newState)
        {
            return ExecutionState switch
            {
                QueueExecutionState.Idle => newState is QueueExecutionState.Starting or QueueExecutionState.Completed,
                QueueExecutionState.Starting => newState is QueueExecutionState.Processing or QueueExecutionState.Stopping or QueueExecutionState.Idle,
                QueueExecutionState.Processing => newState is QueueExecutionState.Transitioning or QueueExecutionState.Paused or QueueExecutionState.Stopping or QueueExecutionState.Completed,
                QueueExecutionState.Transitioning => newState is QueueExecutionState.Processing or QueueExecutionState.Stopping or QueueExecutionState.Completed,
                QueueExecutionState.Paused => newState is QueueExecutionState.Processing or QueueExecutionState.Stopping,
                QueueExecutionState.Stopping => newState is QueueExecutionState.Idle or QueueExecutionState.Completed,
                QueueExecutionState.Completed => newState is QueueExecutionState.Idle or QueueExecutionState.Starting,
                _ => false
            };
        }

        #endregion

        #region Events

        public event EventHandler? QueueStarted;
        public event EventHandler<QueueStoppedEventArgs>? QueueStopped;
        public event EventHandler? QueuePaused;
        public event EventHandler? QueueResumed;
        public event EventHandler<AutoLoginQueueItemEventArgs>? ItemStarted;
        public event EventHandler<AutoLoginQueueItemEventArgs>? ItemCompleted;
        public event EventHandler<AutoLoginQueueItemEventArgs>? ItemFailed;
        public event EventHandler<AutoLoginQueueItemEventArgs>? ItemProgressUpdated;
        public event EventHandler<ProfileSwappedEventArgs>? ProfileSwapped;

        #endregion

        #region Queue Management

        /// <summary>
        /// Checks if the specified account is already in the queue
        /// </summary>
        public bool IsAccountAlreadyQueued(PlayOnlineMemberAccount account)
        {
            if (account == null) return false;

            lock (_lockObject)
            {
                return QueueItems.Any(item => item.Account.Id == account.Id);
            }
        }

        public async Task<AutoLoginQueueItem> AddToQueueAsync(PlayOnlineMemberAccount account, ProfileInfo profile)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            // Check for duplicate account (by GUID) to prevent conflicts
            lock (_lockObject)
            {
                var existingItem = QueueItems.FirstOrDefault(item => item.Account.Id == account.Id);
                if (existingItem != null)
                {
                    throw new InvalidOperationException($"Account '{account.DisplayName}' is already in the queue. Duplicate accounts cannot be added as this would cause login conflicts.");
                }
            }

            var queueItem = new AutoLoginQueueItem
            {
                Account = account,
                Profile = profile,
                Position = TotalItems + 1
            };

            await _uiDispatcher.InvokeAsync(() =>
            {
                lock (_lockObject)
                {
                    QueueItems.Add(queueItem);
                }
            });

            await _loggingService.LogInfoAsync($"Added {account.DisplayName} from profile {profile.Name} to auto-login queue");

            if (AutoSaveQueueState)
            {
                await SaveQueueStateAsync();
            }

            return queueItem;
        }

        public async Task<bool> RemoveFromQueueAsync(AutoLoginQueueItem item)
        {
            if (item == null) return false;

            // Don't allow removal of currently executing item
            if (item.Status == AutoLoginQueueStatus.InProgress)
            {
                await _loggingService.LogWarningAsync("Cannot remove currently executing queue item");
                return false;
            }

            bool removed = false;
            await _uiDispatcher.InvokeAsync(() =>
            {
                lock (_lockObject)
                {
                    removed = QueueItems.Remove(item);
                }
            });

            // Clean up the item's event subscriptions if it was removed
            if (removed)
            {
                item.Cleanup();
            }

            if (removed)
            {
                await _loggingService.LogInfoAsync($"Removed {item.DisplayName} from auto-login queue");

                if (AutoSaveQueueState)
                {
                    await SaveQueueStateAsync();
                }
            }

            return removed;
        }

        public async Task ClearQueueAsync()
        {
            // Don't clear if queue is executing
            if (IsExecuting)
            {
                await _loggingService.LogWarningAsync("Cannot clear queue while execution is in progress");
                return;
            }

            var itemCount = TotalItems;

            // Clean up all items before clearing
            var itemsToCleanup = QueueItems.ToList();

            await _uiDispatcher.InvokeAsync(() =>
            {
                lock (_lockObject)
                {
                    QueueItems.Clear();
                }
            });

            // Clean up event subscriptions for all removed items
            foreach (var item in itemsToCleanup)
            {
                item.Cleanup();
            }

            await _loggingService.LogInfoAsync($"Cleared {itemCount} items from auto-login queue");

            if (AutoSaveQueueState)
            {
                await SaveQueueStateAsync();
            }
        }

        public async Task<bool> MoveItemAsync(AutoLoginQueueItem item, int newPosition)
        {
            if (item == null || newPosition < 1 || newPosition > TotalItems) return false;

            // Don't allow moving currently executing item
            if (item.Status == AutoLoginQueueStatus.InProgress)
            {
                await _loggingService.LogWarningAsync("Cannot move currently executing queue item");
                return false;
            }

            lock (_lockObject)
            {
                var currentIndex = QueueItems.IndexOf(item);
                if (currentIndex == -1) return false;

                var newIndex = newPosition - 1; // Convert to 0-based
                QueueItems.Move(currentIndex, newIndex);
            }

            await _loggingService.LogInfoAsync($"Moved {item.DisplayName} to position {newPosition}");

            if (AutoSaveQueueState)
            {
                await SaveQueueStateAsync();
            }

            return true;
        }

        public async Task ReorderQueueAsync(IList<AutoLoginQueueItem> newOrder)
        {
            if (newOrder == null) return;

            // Don't allow reordering if queue is executing
            if (IsExecuting)
            {
                await _loggingService.LogWarningAsync("Cannot reorder queue while execution is in progress");
                return;
            }

            await _uiDispatcher.InvokeAsync(() =>
            {
                lock (_lockObject)
                {
                    QueueItems.Clear();
                    foreach (var item in newOrder)
                    {
                        QueueItems.Add(item);
                    }
                }
            });

            await _loggingService.LogInfoAsync($"Reordered queue with {newOrder.Count} items");

            if (AutoSaveQueueState)
            {
                await SaveQueueStateAsync();
            }
        }

        #endregion

        #region Execution Control

        public async Task StartQueueAsync(CancellationToken cancellationToken = default)
        {
            if (ExecutionState != QueueExecutionState.Idle)
            {
                await _loggingService.LogWarningAsync($"Cannot start queue: current state is {ExecutionState}");
                return;
            }

            if (TotalItems == 0)
            {
                await _loggingService.LogWarningAsync("Cannot start queue: no items in queue");
                return;
            }

            await _executionSemaphore.WaitAsync(cancellationToken);
            try
            {
                TransitionToState(QueueExecutionState.Starting, "Initializing queue execution");
                _executionCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                // Store original profile
                var activeProfile = await _profileService.GetActiveLoginInfoAsync();
                _originalProfilePath = activeProfile?.FilePath;

                await _loggingService.LogInfoAsync($"Starting auto-login queue with {TotalItems} items");
                QueueStarted?.Invoke(this, EventArgs.Empty);

                // Start execution task
                _executionTask = ExecuteQueueAsync(_executionCancellationTokenSource.Token);
                await _executionTask;
            }
            finally
            {
                _executionSemaphore.Release();
            }
        }

        public async Task StopQueueAsync()
        {
            if (!IsExecuting) return;

            await _loggingService.LogInfoAsync("Stopping auto-login queue");

            _executionCancellationTokenSource?.Cancel();

            if (_executionTask != null)
            {
                try
                {
                    await _executionTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancelled
                }
            }

            await FinishExecution(QueueStopReason.UserRequested, "Stopped by user");
        }

        public async Task PauseQueueAsync()
        {
            if (ExecutionState != QueueExecutionState.Processing) return;

            TransitionToState(QueueExecutionState.Paused, "Queue paused by user");

            // Pause the current task execution
            await _taskExecutor.PauseCurrentTaskAsync();

            await _loggingService.LogInfoAsync("Paused auto-login queue");
            QueuePaused?.Invoke(this, EventArgs.Empty);

            if (AutoSaveQueueState)
            {
                await SaveQueueStateAsync();
            }
        }

        public async Task ResumeQueueAsync()
        {
            if (ExecutionState != QueueExecutionState.Paused) return;

            TransitionToState(QueueExecutionState.Processing, "Queue resumed by user");

            // Resume the current task execution
            await _taskExecutor.ResumeCurrentTaskAsync();

            await _loggingService.LogInfoAsync("Resumed auto-login queue");
            QueueResumed?.Invoke(this, EventArgs.Empty);

            if (AutoSaveQueueState)
            {
                await SaveQueueStateAsync();
            }
        }

        public async Task SkipCurrentItemAsync()
        {
            if (CurrentItem == null || (ExecutionState != QueueExecutionState.Processing && ExecutionState != QueueExecutionState.Paused))
            {
                await _loggingService.LogWarningAsync($"Cannot skip: no current item or invalid state ({ExecutionState})");
                return;
            }

            var skippedItem = CurrentItem;
            skippedItem.Status = AutoLoginQueueStatus.Cancelled;
            skippedItem.StatusMessage = "Skipped by user";
            skippedItem.EndTime = DateTime.Now;

            await _loggingService.LogInfoAsync($"Skipped current queue item: {skippedItem.DisplayName}");

            // Immediately cancel the current item's operation
            _currentItemCancellationTokenSource?.Cancel();
            await _loggingService.LogDebugAsync("Cancelled current item's operation for immediate skip");

            // Transition to transitioning state with clear message
            TransitionToState(QueueExecutionState.Transitioning, $"Skipping to next item after {skippedItem.DisplayName}");

            // Clear the current item so the queue can move to the next pending item
            CurrentItem = null;

            // Trigger completion events and save state
            GetStatisticsInternal().UpdateWithCompletedItem(skippedItem);
            ItemFailed?.Invoke(this, new AutoLoginQueueItemEventArgs(skippedItem, "Skipped by user"));

            if (AutoSaveQueueState)
            {
                await SaveQueueStateAsync();
            }
        }

        public async Task RetryItemAsync(AutoLoginQueueItem item)
        {
            if (item?.Status != AutoLoginQueueStatus.Failed) return;

            item.Reset();
            await _loggingService.LogInfoAsync($"Reset failed item for retry: {item.DisplayName}");

            if (AutoSaveQueueState)
            {
                await SaveQueueStateAsync();
            }
        }

        public async Task ResetQueueAsync()
        {
            if (IsExecuting)
            {
                await _loggingService.LogWarningAsync("Cannot reset queue while execution is in progress");
                return;
            }

            await _uiDispatcher.InvokeAsync(() =>
            {
                lock (_lockObject)
                {
                    // Reset all items back to pending state
                    foreach (var item in QueueItems)
                    {
                        item.Reset();
                    }
                }
            });

            await _loggingService.LogInfoAsync($"Reset {QueueItems.Count} queue items to pending state");

            if (AutoSaveQueueState)
            {
                await SaveQueueStateAsync();
            }
        }

        #endregion

        #region Core Execution

        private async Task ExecuteQueueAsync(CancellationToken cancellationToken)
        {
            try
            {
                var statistics = GetStatisticsInternal();
                statistics.UpdateExecutionStart();

                // Process items dynamically to handle skip requests properly
                while (true)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    // Get next pending item dynamically
                    var nextItem = QueueItems.FirstOrDefault(x => x.Status == AutoLoginQueueStatus.Pending);
                    if (nextItem == null) break; // No more pending items

                    // Wait if paused
                    while (ExecutionState == QueueExecutionState.Paused && !cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(100, cancellationToken);
                    }

                    if (cancellationToken.IsCancellationRequested) break;

                    // Check if item is still pending (could have been skipped/cancelled while paused)
                    if (nextItem.Status != AutoLoginQueueStatus.Pending) continue;

                    // Transition to processing the next item
                    CurrentItem = nextItem;
                    TransitionToState(QueueExecutionState.Processing, $"Processing {nextItem.DisplayName}");

                    await ExecuteQueueItemAsync(nextItem, cancellationToken);

                    // Check if the item was skipped during execution
                    if (nextItem.Status == AutoLoginQueueStatus.Cancelled)
                    {
                        await _loggingService.LogInfoAsync($"Item {nextItem.DisplayName} was skipped, continuing to next item");
                        // State should already be Transitioning from SkipCurrentItemAsync
                        // Continue to next iteration without delay
                        continue;
                    }

                    // Transition between items
                    if (nextItem.Status == AutoLoginQueueStatus.Completed || nextItem.Status == AutoLoginQueueStatus.Failed)
                    {
                        TransitionToState(QueueExecutionState.Transitioning, "Moving to next item");
                    }

                    // Delay between items (only if not skipped)
                    if (DelayBetweenItems > 0 && !cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(DelayBetweenItems, cancellationToken);
                    }

                    // Stop if failed and not configured to continue
                    if (nextItem.Status == AutoLoginQueueStatus.Failed && !ContinueOnFailure)
                    {
                        await FinishExecution(QueueStopReason.Failed, $"Stopped after failure: {nextItem.ErrorMessage}");
                        return;
                    }
                }

                statistics.UpdateExecutionEnd();
                await FinishExecution(QueueStopReason.Completed, "Queue completed successfully");
            }
            catch (OperationCanceledException)
            {
                await FinishExecution(QueueStopReason.Cancelled, "Queue execution was cancelled");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error during queue execution", ex);
                await FinishExecution(QueueStopReason.Failed, $"Queue execution failed: {ex.Message}");
            }
        }

        private async Task ExecuteQueueItemAsync(AutoLoginQueueItem item, CancellationToken cancellationToken)
        {
            // Create per-item cancellation token source
            _currentItemCancellationTokenSource?.Dispose();
            _currentItemCancellationTokenSource = new CancellationTokenSource();

            // Combine queue cancellation token with per-item cancellation token
            using var combinedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _currentItemCancellationTokenSource.Token);
            var combinedToken = combinedTokenSource.Token;

            try
            {
                item.Status = AutoLoginQueueStatus.InProgress;
                item.StartTime = DateTime.Now;
                item.StatusMessage = "Starting login process...";

                await _loggingService.LogInfoAsync($"Starting login for {item.DisplayName}");
                ItemStarted?.Invoke(this, new AutoLoginQueueItemEventArgs(item));

                // Switch profile if needed
                var currentActiveProfile = await _profileService.GetActiveLoginInfoAsync();
                if (item.Profile.FilePath != currentActiveProfile?.FilePath)
                {
                    await _loggingService.LogInfoAsync($"Switching to profile: {item.Profile.Name}");
                    var fromProfile = currentActiveProfile ?? new ProfileInfo { Name = "Unknown", FilePath = "" };
                    await _profileService.SwapProfileAsync(item.Profile);

                    // Notify about profile swap
                    ProfileSwapped?.Invoke(this, new ProfileSwappedEventArgs(fromProfile, item.Profile));
                }

                // Execute login task using the task executor
                await _taskExecutor.ExecuteAsync(item, combinedToken);

                // Mark as completed if we got through all steps
                if (item.Status == AutoLoginQueueStatus.InProgress)
                {
                    item.Status = AutoLoginQueueStatus.Completed;
                    item.EndTime = DateTime.Now;
                    item.StatusMessage = "Login completed successfully";

                    await _loggingService.LogInfoAsync($"Completed login for {item.DisplayName}");
                    ItemCompleted?.Invoke(this, new AutoLoginQueueItemEventArgs(item));
                }
            }
            catch (OperationCanceledException)
            {
                item.Status = AutoLoginQueueStatus.Cancelled;
                item.EndTime = DateTime.Now;
                item.StatusMessage = "Login cancelled";

                // Only re-throw if it's the main queue cancellation, not per-item cancellation
                if (cancellationToken.IsCancellationRequested)
                {
                    // Main queue was cancelled - propagate to stop entire queue
                    throw;
                }

                // Per-item cancellation (skip) - don't re-throw, just log and continue queue
                await _loggingService.LogInfoAsync($"Item {item.DisplayName} was cancelled (skipped), continuing to next item");
            }
            catch (Exception ex)
            {
                item.Status = AutoLoginQueueStatus.Failed;
                item.EndTime = DateTime.Now;
                item.ErrorMessage = ex.Message;
                item.StatusMessage = $"Login failed: {ex.Message}";

                await _loggingService.LogErrorAsync($"Failed login for {item.DisplayName}", ex);
                ItemFailed?.Invoke(this, new AutoLoginQueueItemEventArgs(item, ex.Message));
            }
            finally
            {
                CurrentItem = null;
                GetStatisticsInternal().UpdateWithCompletedItem(item);

                if (AutoSaveQueueState)
                {
                    await SaveQueueStateAsync();
                }
            }
        }

        private async Task ExecuteLoginStepsAsync(AutoLoginQueueItem item, CancellationToken cancellationToken)
        {
            var steps = LoginTaskStepExtensions.GetMainSteps().ToList();

            foreach (var step in steps)
            {
                if (cancellationToken.IsCancellationRequested) break;

                // Wait if paused
                while (IsPaused && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(100, cancellationToken);
                }

                if (cancellationToken.IsCancellationRequested) break;

                item.CurrentStep = step;
                item.CurrentStepProgress = 0;
                item.StatusMessage = step.GetShortDisplayName();

                await _loggingService.LogDebugAsync($"Executing step: {step.GetDisplayName()} for {item.DisplayName}");
                ItemProgressUpdated?.Invoke(this, new AutoLoginQueueItemEventArgs(item));

                // Execute the step (mock implementation)
                await ExecuteSingleLoginStepAsync(item, step, cancellationToken);

                item.CompleteStep(step);
                item.CurrentStepProgress = 100;
                ItemProgressUpdated?.Invoke(this, new AutoLoginQueueItemEventArgs(item));

                // Small delay between steps
                await Task.Delay(500, cancellationToken);
            }
        }

        private async Task ExecuteSingleLoginStepAsync(AutoLoginQueueItem item, LoginTaskStep step, CancellationToken cancellationToken)
        {
            var stepDuration = step.GetEstimatedDurationSeconds() * 1000; // Convert to milliseconds
            var progressInterval = stepDuration / 10; // Update progress 10 times during step

            for (int i = 0; i < 10; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                // Wait if paused
                while (IsPaused && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(100, cancellationToken);
                }

                item.CurrentStepProgress = (i + 1) * 10;
                ItemProgressUpdated?.Invoke(this, new AutoLoginQueueItemEventArgs(item));

                await Task.Delay(progressInterval, cancellationToken);
            }

            // Handle special cases
            switch (step)
            {
                case LoginTaskStep.LaunchWindower:
                    // Execute Windower sub-tasks
                    foreach (var subTask in LoginTaskStepExtensions.GetWindowerSubTasks())
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        item.CurrentStep = subTask;
                        item.StatusMessage = subTask.GetShortDisplayName();
                        await ExecuteSingleLoginStepAsync(item, subTask, cancellationToken);
                        item.CompleteStep(subTask);
                    }
                    break;

                case LoginTaskStep.OTPEntry:
                    // Skip if account doesn't have OTP enabled
                    if (!item.Account.IsOTPEnabled)
                    {
                        await _loggingService.LogDebugAsync($"Skipping OTP step for {item.DisplayName} - OTP not enabled");
                        await Task.Delay(100, cancellationToken); // Minimal delay for skipped step
                    }
                    break;
            }
        }

        #endregion

        #region Persistence

        public async Task SaveQueueStateAsync()
        {
            try
            {
                var settings = _settingsService.LoadSettings();

                settings.QueueState = new AutoLoginQueueState
                {
                    QueueItems = QueueItems.Select(SerializableQueueItem.FromQueueItem).ToList(),
                    WasExecuting = IsExecuting,
                    WasPaused = IsPaused,
                    OriginalProfilePath = _originalProfilePath,
                    LastSaved = DateTime.Now,
                    Statistics = GetStatisticsInternal()
                };

                _settingsService.SaveSettings(settings);
                await _loggingService.LogDebugAsync("Saved auto-login queue state");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error saving queue state", ex);
            }
        }

        public async Task LoadQueueStateAsync()
        {
            try
            {
                var settings = _settingsService.LoadSettings();
                if (settings.QueueState == null) return;

                var queueState = settings.QueueState;

                // Restore queue items
                var restoredItems = new List<AutoLoginQueueItem>();
                foreach (var serializedItem in queueState.QueueItems)
                {
                    var account = await FindAccountById(serializedItem.AccountId, serializedItem.ProfilePath);
                    var profile = await FindProfileByPath(serializedItem.ProfilePath);

                    if (account != null && profile != null)
                    {
                        // Preserve completed status during session, only reset on application restart
                        var restoredStatus = serializedItem.Status == AutoLoginQueueStatus.Completed
                            ? AutoLoginQueueStatus.Completed
                            : AutoLoginQueueStatus.Pending;

                        var item = new AutoLoginQueueItem
                        {
                            Id = serializedItem.Id,
                            Account = account,
                            Profile = profile,
                            Position = serializedItem.Position,
                            Status = restoredStatus,
                            CurrentStep = restoredStatus == AutoLoginQueueStatus.Completed ? serializedItem.CurrentStep : LoginTaskStep.None,
                            CompletedSteps = restoredStatus == AutoLoginQueueStatus.Completed ? new List<LoginTaskStep>(serializedItem.CompletedSteps) : new List<LoginTaskStep>(),
                            StartTime = restoredStatus == AutoLoginQueueStatus.Completed ? serializedItem.StartTime : null,
                            EndTime = restoredStatus == AutoLoginQueueStatus.Completed ? serializedItem.EndTime : null,
                            ErrorMessage = restoredStatus == AutoLoginQueueStatus.Completed ? (serializedItem.ErrorMessage ?? string.Empty) : string.Empty,
                            StatusMessage = restoredStatus == AutoLoginQueueStatus.Completed ? (serializedItem.StatusMessage ?? "Completed") : "Ready to start"
                        };

                        restoredItems.Add(item);
                    }
                }

                await _uiDispatcher.InvokeAsync(() =>
                {
                    lock (_lockObject)
                    {
                        QueueItems.Clear();
                        foreach (var item in restoredItems.OrderBy(x => x.Position))
                        {
                            QueueItems.Add(item);
                        }
                    }
                });

                // Log the reset behavior for user awareness
                if (restoredItems.Any())
                {
                    await _loggingService.LogInfoAsync($"Queue state loaded: {restoredItems.Count} items reset to pending for fresh auto-login session");
                }

                _originalProfilePath = queueState.OriginalProfilePath;

                await _loggingService.LogInfoAsync($"Loaded auto-login queue state with {restoredItems.Count} items");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error loading queue state", ex);
            }
        }

        public QueueStatistics GetStatistics()
        {
            var internalStats = GetStatisticsInternal();
            return new QueueStatistics
            {
                TotalItems = TotalItems,
                CompletedItems = CompletedItems,
                FailedItems = FailedItems,
                PendingItems = QueueItems.Count(x => x.Status == AutoLoginQueueStatus.Pending),
                TotalExecutionTime = internalStats.TotalExecutionTime,
                AverageItemTime = internalStats.AverageItemTime,
                SuccessRate = internalStats.SuccessRate,
                LastExecutionStart = internalStats.LastExecutionStart,
                LastExecutionEnd = internalStats.LastExecutionEnd
            };
        }

        #endregion

        #region Helper Methods

        private void LoadConfigurationFromSettings()
        {
            var settings = _settingsService.LoadSettings();
            AutoSaveQueueState = settings.AutoSaveQueueState;
            ContinueOnFailure = settings.ContinueQueueOnFailure;
            DelayBetweenItems = settings.QueueDelayBetweenItemsMs;
            StepTimeoutSeconds = settings.LoginStepTimeoutSeconds;
        }

        private void UpdateQueuePositions()
        {
            lock (_lockObject)
            {
                for (int i = 0; i < QueueItems.Count; i++)
                {
                    QueueItems[i].Position = i + 1;
                }
            }
        }

        private async Task FinishExecution(QueueStopReason reason, string message)
        {
            // Transition to stopping state first
            TransitionToState(QueueStopReason.Completed == reason ? QueueExecutionState.Completed : QueueExecutionState.Stopping,
                $"Finishing execution: {message}");

            CurrentItem = null;

            // If user manually stopped the queue, reset all items to provide a clean restart
            if (reason == QueueStopReason.UserRequested)
            {
                await _uiDispatcher.InvokeAsync(() =>
                {
                    lock (_lockObject)
                    {
                        // Reset all non-completed items back to pending state
                        foreach (var item in QueueItems.Where(x => x.Status != AutoLoginQueueStatus.Completed))
                        {
                            item.Reset();
                        }
                    }
                });

                await _loggingService.LogInfoAsync("Reset queue items to pending state for clean restart");
            }

            // Restore original profile if configured
            var settings = _settingsService.LoadSettings();
            if (settings.RestoreOriginalProfileAfterQueue && !string.IsNullOrEmpty(_originalProfilePath))
            {
                try
                {
                    var originalProfile = await FindProfileByPath(_originalProfilePath);
                    if (originalProfile != null)
                    {
                        await _profileService.SwapProfileAsync(originalProfile);
                        await _loggingService.LogInfoAsync($"Restored original profile: {originalProfile.Name}");
                    }
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync("Error restoring original profile", ex);
                }
            }

            _originalProfilePath = null;

            if (AutoSaveQueueState)
            {
                await SaveQueueStateAsync();
            }

            // Final transition to idle state
            TransitionToState(QueueExecutionState.Idle, "Queue execution finished");

            QueueStopped?.Invoke(this, new QueueStoppedEventArgs(reason, message));
        }

        private QueueExecutionStatistics GetStatisticsInternal()
        {
            var settings = _settingsService.LoadSettings();
            return settings.QueueState?.Statistics ?? new QueueExecutionStatistics();
        }

        private async Task<PlayOnlineMemberAccount?> FindAccountById(Guid accountId, string profilePath)
        {
            try
            {
                var accounts = await _accountService.GetAccountsForProfileAsync(profilePath);
                return accounts.FirstOrDefault(a => a.Id == accountId);
            }
            catch
            {
                return null;
            }
        }

        private async Task<ProfileInfo?> FindProfileByPath(string profilePath)
        {
            try
            {
                var profiles = await _profileService.GetProfilesAsync();
                return profiles.FirstOrDefault(p => p.FilePath == profilePath);
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Task Executor Event Handling

        /// <summary>
        /// Sets up event handlers for the task executor to enhance progress reporting
        /// </summary>
        private void SetupTaskExecutorEventHandlers()
        {
            // Task-level events
            _taskExecutor.TaskStarted += OnTaskExecutorTaskStarted;
            _taskExecutor.TaskCompleted += OnTaskExecutorTaskCompleted;
            _taskExecutor.TaskFailed += OnTaskExecutorTaskFailed;
            _taskExecutor.TaskProgressUpdated += OnTaskExecutorTaskProgressUpdated;

            // Subtask-level events
            _taskExecutor.SubtaskStarted += OnTaskExecutorSubtaskStarted;
            _taskExecutor.SubtaskCompleted += OnTaskExecutorSubtaskCompleted;
            _taskExecutor.SubtaskFailed += OnTaskExecutorSubtaskFailed;
            _taskExecutor.SubtaskProgressUpdated += OnTaskExecutorSubtaskProgressUpdated;
        }

        /// <summary>
        /// Cleans up task executor event handlers
        /// </summary>
        private void CleanupTaskExecutorEventHandlers()
        {
            // Task-level events
            _taskExecutor.TaskStarted -= OnTaskExecutorTaskStarted;
            _taskExecutor.TaskCompleted -= OnTaskExecutorTaskCompleted;
            _taskExecutor.TaskFailed -= OnTaskExecutorTaskFailed;
            _taskExecutor.TaskProgressUpdated -= OnTaskExecutorTaskProgressUpdated;

            // Subtask-level events
            _taskExecutor.SubtaskStarted -= OnTaskExecutorSubtaskStarted;
            _taskExecutor.SubtaskCompleted -= OnTaskExecutorSubtaskCompleted;
            _taskExecutor.SubtaskFailed -= OnTaskExecutorSubtaskFailed;
            _taskExecutor.SubtaskProgressUpdated -= OnTaskExecutorSubtaskProgressUpdated;
        }

        // Task-level event handlers
        private void OnTaskExecutorTaskStarted(object? sender, AutoLoginTaskEventArgs e)
        {
            _ = Task.Run(async () => _loggingService.LogDebugAsync($"Task started: {e.Task.Name} for {e.QueueItem.DisplayName}"));
        }

        private void OnTaskExecutorTaskCompleted(object? sender, AutoLoginTaskEventArgs e)
        {
            _ = Task.Run(async () => _loggingService.LogDebugAsync($"Task completed: {e.Task.Name} for {e.QueueItem.DisplayName}"));
        }

        private void OnTaskExecutorTaskFailed(object? sender, AutoLoginTaskEventArgs e)
        {
            _ = Task.Run(async () => _loggingService.LogWarningAsync($"Task failed: {e.Task.Name} for {e.QueueItem.DisplayName} - {e.Message}"));
        }

        private void OnTaskExecutorTaskProgressUpdated(object? sender, AutoLoginTaskEventArgs e)
        {
            // Update queue item progress and notify UI
            ItemProgressUpdated?.Invoke(this, new AutoLoginQueueItemEventArgs(e.QueueItem, e.Message));
        }

        // Subtask-level event handlers
        private void OnTaskExecutorSubtaskStarted(object? sender, AutoLoginSubtaskEventArgs e)
        {
            _ = Task.Run(async () => _loggingService.LogDebugAsync($"Subtask started: {e.Subtask.Name} for {e.QueueItem.DisplayName}"));

            // Update legacy queue item properties for backward compatibility
            e.QueueItem.CurrentStep = e.Subtask.TaskStep;
            e.QueueItem.StatusMessage = e.Subtask.StatusMessage;
        }

        private void OnTaskExecutorSubtaskCompleted(object? sender, AutoLoginSubtaskEventArgs e)
        {
            _ = Task.Run(async () => _loggingService.LogDebugAsync($"Subtask completed: {e.Subtask.Name} for {e.QueueItem.DisplayName}"));

            // Update legacy completed steps for backward compatibility
            e.QueueItem.CompleteStep(e.Subtask.TaskStep);
        }

        private void OnTaskExecutorSubtaskFailed(object? sender, AutoLoginSubtaskEventArgs e)
        {
            _ = Task.Run(async () => _loggingService.LogWarningAsync($"Subtask failed: {e.Subtask.Name} for {e.QueueItem.DisplayName} - {e.Message}"));
        }

        private void OnTaskExecutorSubtaskProgressUpdated(object? sender, AutoLoginSubtaskEventArgs e)
        {
            _ = Task.Run(async () => _loggingService.LogDebugAsync($"Subtask progress: {e.Subtask.Name} - {e.Subtask.Progress}% for {e.QueueItem.DisplayName}"));

            // Update legacy queue item progress for backward compatibility
            e.QueueItem.CurrentStepProgress = e.Subtask.Progress;

            // Notify UI of progress update
            ItemProgressUpdated?.Invoke(this, new AutoLoginQueueItemEventArgs(e.QueueItem, $"{e.Subtask.Name}: {e.Subtask.Progress}%"));
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            // Clean up task executor event handlers
            CleanupTaskExecutorEventHandlers();

            // Cancel any running execution
            _executionCancellationTokenSource?.Cancel();
            _currentItemCancellationTokenSource?.Cancel();

            // Wait for execution to complete
            try
            {
                _executionTask?.Wait(5000); // Wait up to 5 seconds
            }
            catch
            {
                // Ignore exceptions during disposal
            }

            _executionCancellationTokenSource?.Dispose();
            _currentItemCancellationTokenSource?.Dispose();
            _executionSemaphore?.Dispose();

            // Task executor disposal handled by DI container

            GC.SuppressFinalize(this);
        }

        #endregion
    }
}