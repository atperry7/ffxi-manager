using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services
{
    /// <summary>
    /// Implementation of queue execution orchestration and control
    /// </summary>
    public class QueueExecutionOrchestrator : IQueueExecutionOrchestrator, IDisposable
    {
        private readonly ISettingsService _settingsService;
        private readonly SemaphoreSlim _executionSemaphore = new(1, 1);
        private CancellationTokenSource? _executionCancellationTokenSource;
        private CancellationTokenSource? _currentItemCancellationTokenSource;
        private Task? _executionTask;
        private QueueExecutionContext? _currentContext;
        private bool _disposed;

        public QueueExecutionOrchestrator(ISettingsService settingsService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            LoadConfigurationFromSettings();
        }

        #region Configuration

        public bool ContinueOnFailure { get; set; } = true;
        public int DelayBetweenItems { get; set; } = 2000;
        public int StepTimeoutSeconds { get; set; } = 30;
        public bool RestoreOriginalProfileAfterQueue { get; set; } = true;

        #endregion

        #region Events

        public event EventHandler? ExecutionStarted;
        public event EventHandler<QueueStoppedEventArgs>? ExecutionStopped;
        public event EventHandler? ExecutionPaused;
        public event EventHandler? ExecutionResumed;
        public event EventHandler<AutoLoginQueueItemEventArgs>? ItemStarted;
        public event EventHandler<AutoLoginQueueItemEventArgs>? ItemCompleted;
        public event EventHandler<AutoLoginQueueItemEventArgs>? ItemFailed;
        public event EventHandler<AutoLoginQueueItemEventArgs>? ItemProgressUpdated;
        public event EventHandler<ProfileSwappedEventArgs>? ProfileSwapped;

        #endregion

        #region Execution Control

        public async Task StartExecutionAsync(QueueExecutionContext context, CancellationToken cancellationToken = default)
        {
            if (!context.StateMachine.CanStart())
            {
                await context.LoggingService.LogWarningAsync($"Cannot start queue: current state is {context.StateMachine.ExecutionState}");
                return;
            }

            if (context.CollectionManager.TotalItems == 0)
            {
                await context.LoggingService.LogWarningAsync("Cannot start queue: no items in queue");
                return;
            }

            await _executionSemaphore.WaitAsync(cancellationToken);
            try
            {
                _currentContext = context;
                context.StateMachine.TryTransitionTo(QueueExecutionState.Starting, "Initializing queue execution");
                _executionCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                // Store original profile
                var activeProfile = await context.ProfileService.GetActiveLoginInfoAsync();
                context.OriginalProfilePath = activeProfile?.FilePath;

                await context.LoggingService.LogInfoAsync($"Starting auto-login queue with {context.CollectionManager.TotalItems} items");
                ExecutionStarted?.Invoke(this, EventArgs.Empty);

                // Start execution task
                _executionTask = ExecuteQueueAsync(context, _executionCancellationTokenSource.Token);
                await _executionTask;
            }
            finally
            {
                _executionSemaphore.Release();
            }
        }

        public async Task StopExecutionAsync()
        {
            if (_currentContext?.StateMachine.CanStop() != true) return;

            await _currentContext.LoggingService.LogInfoAsync("Stopping auto-login queue");

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

        public async Task PauseExecutionAsync()
        {
            if (_currentContext?.StateMachine.CanPause() != true) return;

            _currentContext.StateMachine.TryTransitionTo(QueueExecutionState.Paused, "Queue paused by user");

            // Pause the current task execution
            await _currentContext.TaskExecutor.PauseCurrentTaskAsync();

            await _currentContext.LoggingService.LogInfoAsync("Paused auto-login queue");
            ExecutionPaused?.Invoke(this, EventArgs.Empty);

            if (_currentContext.PersistenceService.AutoSaveQueueState)
            {
                await SaveQueueStateAsync();
            }
        }

        public async Task ResumeExecutionAsync()
        {
            if (_currentContext?.StateMachine.CanResume() != true) return;

            _currentContext.StateMachine.TryTransitionTo(QueueExecutionState.Processing, "Queue resumed by user");

            // Resume the current task execution
            await _currentContext.TaskExecutor.ResumeCurrentTaskAsync();

            await _currentContext.LoggingService.LogInfoAsync("Resumed auto-login queue");
            ExecutionResumed?.Invoke(this, EventArgs.Empty);

            if (_currentContext.PersistenceService.AutoSaveQueueState)
            {
                await SaveQueueStateAsync();
            }
        }

        public async Task SkipCurrentItemAsync(AutoLoginQueueItem currentItem)
        {
            if (_currentContext?.StateMachine.CanSkipCurrentItem() != true)
            {
                await _currentContext?.LoggingService.LogWarningAsync($"Cannot skip: invalid state ({_currentContext?.StateMachine.ExecutionState})");
                return;
            }

            currentItem.Status = AutoLoginQueueStatus.Cancelled;
            currentItem.StatusMessage = "Skipped by user";
            currentItem.EndTime = DateTime.Now;

            await _currentContext.LoggingService.LogInfoAsync($"Skipped current queue item: {currentItem.DisplayName}");

            // Immediately cancel the current item's operation
            _currentItemCancellationTokenSource?.Cancel();
            await _currentContext.LoggingService.LogDebugAsync("Cancelled current item's operation for immediate skip");

            // Transition to transitioning state with clear message
            _currentContext.StateMachine.TryTransitionTo(QueueExecutionState.Transitioning, $"Skipping to next item after {currentItem.DisplayName}");

            // Clear the current item so the queue can move to the next pending item
            _currentContext.StateMachine.CurrentItem = null;

            // Trigger completion events and save state
            _currentContext.StatisticsService.UpdateWithCompletedItem(currentItem);
            ItemFailed?.Invoke(this, new AutoLoginQueueItemEventArgs(currentItem, "Skipped by user"));

            if (_currentContext.PersistenceService.AutoSaveQueueState)
            {
                await SaveQueueStateAsync();
            }
        }

        #endregion

        #region Core Execution

        private async Task ExecuteQueueAsync(QueueExecutionContext context, CancellationToken cancellationToken)
        {
            try
            {
                context.StatisticsService.RecordExecutionStart();

                // Process items dynamically to handle skip requests properly
                while (true)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    // Get next pending item dynamically
                    var nextItem = context.CollectionManager.QueueItems.FirstOrDefault(x => x.Status == AutoLoginQueueStatus.Pending);
                    if (nextItem == null) break; // No more pending items

                    // Wait if paused
                    while (context.StateMachine.ExecutionState == QueueExecutionState.Paused && !cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(100, cancellationToken);
                    }

                    if (cancellationToken.IsCancellationRequested) break;

                    // Check if item is still pending (could have been skipped/cancelled while paused)
                    if (nextItem.Status != AutoLoginQueueStatus.Pending) continue;

                    // Transition to processing the next item
                    context.StateMachine.CurrentItem = nextItem;
                    context.StateMachine.TryTransitionTo(QueueExecutionState.Processing, $"Processing {nextItem.DisplayName}");

                    await ExecuteQueueItemAsync(context, nextItem, cancellationToken);

                    // Check if the item was skipped during execution
                    if (nextItem.Status == AutoLoginQueueStatus.Cancelled)
                    {
                        await context.LoggingService.LogInfoAsync($"Item {nextItem.DisplayName} was skipped, continuing to next item");
                        continue;
                    }

                    // Transition between items
                    if (nextItem.Status == AutoLoginQueueStatus.Completed || nextItem.Status == AutoLoginQueueStatus.Failed)
                    {
                        context.StateMachine.TryTransitionTo(QueueExecutionState.Transitioning, "Moving to next item");
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

                context.StatisticsService.RecordExecutionEnd();
                await FinishExecution(QueueStopReason.Completed, "Queue completed successfully");
            }
            catch (OperationCanceledException)
            {
                await FinishExecution(QueueStopReason.Cancelled, "Queue execution was cancelled");
            }
            catch (Exception ex)
            {
                await context.LoggingService.LogErrorAsync("Error during queue execution", ex);
                await FinishExecution(QueueStopReason.Failed, $"Queue execution failed: {ex.Message}");
            }
        }

        private async Task ExecuteQueueItemAsync(QueueExecutionContext context, AutoLoginQueueItem item, CancellationToken cancellationToken)
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

                await context.LoggingService.LogInfoAsync($"Starting login for {item.DisplayName}");
                ItemStarted?.Invoke(this, new AutoLoginQueueItemEventArgs(item));

                // Switch profile if needed
                var currentActiveProfile = await context.ProfileService.GetActiveLoginInfoAsync();
                if (item.Profile.FilePath != currentActiveProfile?.FilePath)
                {
                    await context.LoggingService.LogInfoAsync($"Switching to profile: {item.Profile.Name}");
                    var fromProfile = currentActiveProfile ?? new ProfileInfo { Name = "Unknown", FilePath = "" };
                    await context.ProfileService.SwapProfileAsync(item.Profile);

                    // Notify about profile swap
                    ProfileSwapped?.Invoke(this, new ProfileSwappedEventArgs(fromProfile, item.Profile));
                }

                // Execute login task using the task executor
                await context.TaskExecutor.ExecuteAsync(item, combinedToken);

                // Mark as completed if we got through all steps
                if (item.Status == AutoLoginQueueStatus.InProgress)
                {
                    item.Status = AutoLoginQueueStatus.Completed;
                    item.EndTime = DateTime.Now;
                    item.StatusMessage = "Login completed successfully";

                    await context.LoggingService.LogInfoAsync($"Completed login for {item.DisplayName}");
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
                    throw;
                }

                await context.LoggingService.LogInfoAsync($"Item {item.DisplayName} was cancelled (skipped), continuing to next item");
            }
            catch (Exception ex)
            {
                item.Status = AutoLoginQueueStatus.Failed;
                item.EndTime = DateTime.Now;
                item.ErrorMessage = ex.Message;
                item.StatusMessage = $"Login failed: {ex.Message}";

                await context.LoggingService.LogErrorAsync($"Failed login for {item.DisplayName}", ex);
                ItemFailed?.Invoke(this, new AutoLoginQueueItemEventArgs(item, ex.Message));
            }
            finally
            {
                context.StateMachine.CurrentItem = null;
                context.StatisticsService.UpdateWithCompletedItem(item);

                if (context.PersistenceService.AutoSaveQueueState)
                {
                    await SaveQueueStateAsync();
                }
            }
        }

        #endregion

        #region Helper Methods

        private async Task FinishExecution(QueueStopReason reason, string message)
        {
            if (_currentContext == null) return;

            // Transition to stopping state first
            var finalState = reason == QueueStopReason.Completed ? QueueExecutionState.Completed : QueueExecutionState.Stopping;
            _currentContext.StateMachine.TryTransitionTo(finalState, $"Finishing execution: {message}");

            _currentContext.StateMachine.CurrentItem = null;

            // If user manually stopped the queue, reset all items to provide a clean restart
            if (reason == QueueStopReason.UserRequested)
            {
                await _currentContext.CollectionManager.ResetQueueAsync();
                await _currentContext.LoggingService.LogInfoAsync("Reset queue items to pending state for clean restart");
            }

            // Restore original profile if configured
            if (RestoreOriginalProfileAfterQueue && !string.IsNullOrEmpty(_currentContext.OriginalProfilePath))
            {
                try
                {
                    var originalProfile = await _currentContext.PersistenceService.FindProfileByPathAsync(_currentContext.OriginalProfilePath);
                    if (originalProfile != null)
                    {
                        await _currentContext.ProfileService.SwapProfileAsync(originalProfile);
                        await _currentContext.LoggingService.LogInfoAsync($"Restored original profile: {originalProfile.Name}");
                    }
                }
                catch (Exception ex)
                {
                    await _currentContext.LoggingService.LogErrorAsync("Error restoring original profile", ex);
                }
            }

            _currentContext.OriginalProfilePath = null;

            if (_currentContext.PersistenceService.AutoSaveQueueState)
            {
                await SaveQueueStateAsync();
            }

            // Final transition to idle state
            _currentContext.StateMachine.TryTransitionTo(QueueExecutionState.Idle, "Queue execution finished");

            ExecutionStopped?.Invoke(this, new QueueStoppedEventArgs(reason, message));
            _currentContext = null;
        }

        private async Task SaveQueueStateAsync()
        {
            if (_currentContext == null) return;

            await _currentContext.PersistenceService.SaveQueueStateAsync(
                _currentContext.CollectionManager.QueueItems,
                _currentContext.StateMachine.IsExecuting,
                _currentContext.StateMachine.IsPaused,
                _currentContext.OriginalProfilePath,
                _currentContext.StatisticsService.GetExecutionStatistics());
        }

        private void LoadConfigurationFromSettings()
        {
            var settings = _settingsService.LoadSettings();
            ContinueOnFailure = settings.ContinueQueueOnFailure;
            DelayBetweenItems = settings.QueueDelayBetweenItemsMs;
            StepTimeoutSeconds = settings.LoginStepTimeoutSeconds;
            RestoreOriginalProfileAfterQueue = settings.RestoreOriginalProfileAfterQueue;
        }

        #endregion

        #region Disposal

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

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

            GC.SuppressFinalize(this);
        }

        #endregion
    }
}