using FFXIManager.Infrastructure;
using FFXIManager.Models;
using FFXIManager.Models.Settings;
using System.Collections.ObjectModel;

namespace FFXIManager.Services
{
    /// <summary>
    /// Coordinating facade service for auto-login queue operations.
    /// Composes focused services following SOLID principles and maintains backward compatibility.
    /// </summary>
    public class AutoLoginQueueService : IAutoLoginQueueService, IDisposable
    {
        private readonly IQueueCollectionManager _collectionManager;
        private readonly IQueueStateMachine _stateMachine;
        private readonly IQueuePersistenceService _persistenceService;
        private readonly IQueueStatisticsService _statisticsService;
        private readonly IQueueExecutionOrchestrator _executionOrchestrator;
        private readonly IAutoLoginTaskExecutor _taskExecutor;
        private readonly IProfileService _profileService;
        private readonly ILoggingService _loggingService;
        private readonly IUiDispatcher _uiDispatcher;
        private bool _disposed;

        public AutoLoginQueueService(
            IQueueCollectionManager collectionManager,
            IQueueStateMachine stateMachine,
            IQueuePersistenceService persistenceService,
            IQueueStatisticsService statisticsService,
            IQueueExecutionOrchestrator executionOrchestrator,
            IAutoLoginTaskExecutor taskExecutor,
            IProfileService profileService,
            ILoggingService loggingService,
            IUiDispatcher uiDispatcher)
        {
            _collectionManager = collectionManager ?? throw new ArgumentNullException(nameof(collectionManager));
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _persistenceService = persistenceService ?? throw new ArgumentNullException(nameof(persistenceService));
            _statisticsService = statisticsService ?? throw new ArgumentNullException(nameof(statisticsService));
            _executionOrchestrator = executionOrchestrator ?? throw new ArgumentNullException(nameof(executionOrchestrator));
            _taskExecutor = taskExecutor ?? throw new ArgumentNullException(nameof(taskExecutor));
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));

            // Wire up event forwarding to maintain backward compatibility
            SetupEventForwarding();

            // Load configuration and state
            LoadConfigurationFromPersistenceService();
        }

        #region Properties - Delegated to Focused Services

        public ObservableCollection<AutoLoginQueueItem> QueueItems => _collectionManager.QueueItems;

        public bool IsExecuting => _stateMachine.IsExecuting;

        public bool IsPaused => _stateMachine.IsPaused;

        public AutoLoginQueueItem? CurrentItem => _stateMachine.CurrentItem;

        public QueueExecutionState ExecutionState => _stateMachine.ExecutionState;

        public string TransitioningMessage => _stateMachine.TransitioningMessage;

        public int TotalItems => _collectionManager.TotalItems;

        public int CompletedItems => _collectionManager.CompletedItems;

        public int FailedItems => _collectionManager.FailedItems;

        public int CancelledItems => _collectionManager.CancelledItems;

        public int ProcessedItems => _collectionManager.ProcessedItems;

        public int OverallProgress => _collectionManager.OverallProgress;

        public bool AutoSaveQueueState
        {
            get => _persistenceService.AutoSaveQueueState;
            set => _persistenceService.AutoSaveQueueState = value;
        }

        public bool ContinueOnFailure
        {
            get => _executionOrchestrator.ContinueOnFailure;
            set => _executionOrchestrator.ContinueOnFailure = value;
        }

        public int DelayBetweenItems
        {
            get => _executionOrchestrator.DelayBetweenItems;
            set => _executionOrchestrator.DelayBetweenItems = value;
        }

        public int StepTimeoutSeconds
        {
            get => _executionOrchestrator.StepTimeoutSeconds;
            set => _executionOrchestrator.StepTimeoutSeconds = value;
        }

        // Canonical account sync is handled in PlayOnlineMemberAccountService; no queue-level rebinds required

        #endregion

        #region Events - Forwarded from Focused Services

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

        #region Queue Management - Delegated to Collection Manager

        public bool IsAccountAlreadyQueued(PlayOnlineMemberAccount account)
        {
            return _collectionManager.IsAccountAlreadyQueued(account);
        }

        public async Task<AutoLoginQueueItem> AddToQueueAsync(PlayOnlineMemberAccount account, ProfileInfo profile)
        {
            var item = await _collectionManager.AddToQueueAsync(account, profile);

            if (AutoSaveQueueState)
            {
                await SaveQueueStateAsync();
            }

            return item;
        }

        public async Task<bool> RemoveFromQueueAsync(AutoLoginQueueItem item)
        {
            var removed = await _collectionManager.RemoveFromQueueAsync(item);

            if (removed && AutoSaveQueueState)
            {
                await SaveQueueStateAsync();
            }

            return removed;
        }

        public async Task ClearQueueAsync()
        {
            await _collectionManager.ClearQueueAsync();

            if (AutoSaveQueueState)
            {
                await SaveQueueStateAsync();
            }
        }

        public async Task<bool> MoveItemAsync(AutoLoginQueueItem item, int newPosition)
        {
            var moved = await _collectionManager.MoveItemAsync(item, newPosition);

            if (moved && AutoSaveQueueState)
            {
                await SaveQueueStateAsync();
            }

            return moved;
        }

        public async Task ReorderQueueAsync(IList<AutoLoginQueueItem> newOrder)
        {
            await _collectionManager.ReorderQueueAsync(newOrder);

            if (AutoSaveQueueState)
            {
                await SaveQueueStateAsync();
            }
        }

        #endregion

        #region Execution Control - Delegated to Execution Orchestrator

        public async Task StartQueueAsync(CancellationToken cancellationToken = default)
        {
            var context = new QueueExecutionContext
            {
                CollectionManager = _collectionManager,
                StateMachine = _stateMachine,
                StatisticsService = _statisticsService,
                PersistenceService = _persistenceService,
                TaskExecutor = _taskExecutor,
                ProfileService = _profileService,
                LoggingService = _loggingService,
                OriginalProfilePath = null
            };

            await _executionOrchestrator.StartExecutionAsync(context, cancellationToken);
        }

        public async Task StopQueueAsync()
        {
            await _executionOrchestrator.StopExecutionAsync();
        }

        public async Task PauseQueueAsync()
        {
            await _executionOrchestrator.PauseExecutionAsync();
        }

        public async Task ResumeQueueAsync()
        {
            await _executionOrchestrator.ResumeExecutionAsync();
        }

        public async Task SkipCurrentItemAsync()
        {
            if (CurrentItem != null)
            {
                await _executionOrchestrator.SkipCurrentItemAsync(CurrentItem);
            }
        }

        public async Task RetryItemAsync(AutoLoginQueueItem item)
        {
            await _collectionManager.RetryItemAsync(item);

            if (AutoSaveQueueState)
            {
                await SaveQueueStateAsync();
            }
        }

        public async Task ResetQueueAsync()
        {
            await _collectionManager.ResetQueueAsync();

            if (AutoSaveQueueState)
            {
                await SaveQueueStateAsync();
            }
        }

        #endregion

        #region Immediate Login

        public async Task StartImmediateLoginAsync(PlayOnlineMemberAccount account, ProfileInfo profile, CancellationToken cancellationToken = default)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            // If queue is already executing, do not interrupt
            if (IsExecuting)
            {
                throw new InvalidOperationException("Queue is already executing");
            }

            // Ensure item exists in queue
            var targetItem = QueueItems.FirstOrDefault(i => i.Account.Id == account.Id);
            if (targetItem == null)
            {
                targetItem = await AddToQueueAsync(account, profile);
            }

            await PrioritizeSingleItemAndStartAsync(targetItem, cancellationToken);
        }

        public async Task StartImmediateLoginAsync(AutoLoginQueueItem item, CancellationToken cancellationToken = default)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            if (IsExecuting)
            {
                throw new InvalidOperationException("Queue is already executing");
            }

            await PrioritizeSingleItemAndStartAsync(item, cancellationToken);
        }

        private async Task PrioritizeSingleItemAndStartAsync(AutoLoginQueueItem item, CancellationToken cancellationToken)
        {
            // Mark all other pending items as completed (skipped)
            foreach (var qi in QueueItems.ToList())
            {
                if (qi.Id != item.Id && qi.Status == AutoLoginQueueStatus.Pending)
                {
                    qi.Status = AutoLoginQueueStatus.Completed;
                    qi.StatusMessage = "Skipped - Login Now used for another account";
                    qi.EndTime = DateTime.Now;
                }
            }

            // Ensure the selected item is pending
            if (item.Status != AutoLoginQueueStatus.Pending)
            {
                item.Status = AutoLoginQueueStatus.Pending;
                item.StatusMessage = "Prioritized for immediate login";
                item.EndTime = null;
            }

            await StartQueueAsync(cancellationToken);
        }

        #endregion

        #region Persistence - Delegated to Persistence Service

        public async Task SaveQueueStateAsync()
        {
            await _persistenceService.SaveQueueStateAsync(
                QueueItems,
                IsExecuting,
                IsPaused,
                null, // Original profile path is managed by orchestrator
                _statisticsService.GetExecutionStatistics());

            // Save all-time statistics
            await _persistenceService.SaveAllTimeStatisticsAsync(_statisticsService.GetAllTimeStatistics());
        }

        public async Task LoadQueueStateAsync()
        {
            var (items, originalProfilePath) = await _persistenceService.LoadQueueStateAsync();

            // Load all-time statistics
            var allTimeStats = await _persistenceService.LoadAllTimeStatisticsAsync();
            if (allTimeStats != null)
            {
                _statisticsService.LoadAllTimeStatistics(allTimeStats);
            }

            // Add restored items to collection manager using UI dispatcher
            await _uiDispatcher.InvokeAsync(() =>
            {
                foreach (var item in items)
                {
                    _collectionManager.QueueItems.Add(item);
                }
            });
        }

        public QueueStatistics GetStatistics()
        {
            return _statisticsService.CalculateStatistics(QueueItems);
        }

        public QueueExecutionStatistics GetExecutionStatistics()
        {
            return _statisticsService.GetExecutionStatistics();
        }

        public QueueExecutionStatistics GetAllTimeStatistics()
        {
            return _statisticsService.GetAllTimeStatistics();
        }

        #endregion

        #region Event Forwarding Setup

        private void SetupEventForwarding()
        {
            // Forward orchestrator events
            _executionOrchestrator.ExecutionStarted += (sender, e) => QueueStarted?.Invoke(this, e);
            _executionOrchestrator.ExecutionStopped += (sender, e) => QueueStopped?.Invoke(this, e);
            _executionOrchestrator.ExecutionPaused += (sender, e) => QueuePaused?.Invoke(this, e);
            _executionOrchestrator.ExecutionResumed += (sender, e) => QueueResumed?.Invoke(this, e);
            _executionOrchestrator.ItemStarted += (sender, e) => ItemStarted?.Invoke(this, e);
            _executionOrchestrator.ItemCompleted += (sender, e) => ItemCompleted?.Invoke(this, e);
            _executionOrchestrator.ItemFailed += (sender, e) => ItemFailed?.Invoke(this, e);
            _executionOrchestrator.ProfileSwapped += (sender, e) => ProfileSwapped?.Invoke(this, e);

            // Forward task executor events for enhanced progress reporting
            SetupTaskExecutorEventHandlers();
        }

        private void SetupTaskExecutorEventHandlers()
        {
            // Task-level events - these provide enhanced progress reporting
            _taskExecutor.TaskStarted += OnTaskExecutorTaskStarted;
            _taskExecutor.TaskCompleted += OnTaskExecutorTaskCompleted;
            _taskExecutor.TaskFailed += OnTaskExecutorTaskFailed;
            _taskExecutor.TaskProgressUpdated += OnTaskExecutorTaskProgressUpdated;

            // Subtask-level events - these update legacy queue item properties for backward compatibility
            _taskExecutor.SubtaskStarted += OnTaskExecutorSubtaskStarted;
            _taskExecutor.SubtaskCompleted += OnTaskExecutorSubtaskCompleted;
            _taskExecutor.SubtaskFailed += OnTaskExecutorSubtaskFailed;
            _taskExecutor.SubtaskProgressUpdated += OnTaskExecutorSubtaskProgressUpdated;
        }

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
            _ = _loggingService.LogDebugAsync($"Task started: {e.Task.Name} for {e.QueueItem.DisplayName}");
        }

        private void OnTaskExecutorTaskCompleted(object? sender, AutoLoginTaskEventArgs e)
        {
            _ = _loggingService.LogDebugAsync($"Task completed: {e.Task.Name} for {e.QueueItem.DisplayName}");
        }

        private void OnTaskExecutorTaskFailed(object? sender, AutoLoginTaskEventArgs e)
        {
            _ = _loggingService.LogWarningAsync($"Task failed: {e.Task.Name} for {e.QueueItem.DisplayName} - {e.Message}");
        }

        private void OnTaskExecutorTaskProgressUpdated(object? sender, AutoLoginTaskEventArgs e)
        {
            // Update queue item progress and notify UI
            ItemProgressUpdated?.Invoke(this, new AutoLoginQueueItemEventArgs(e.QueueItem, e.Message));
        }

        // Subtask-level event handlers
        private void OnTaskExecutorSubtaskStarted(object? sender, AutoLoginSubtaskEventArgs e)
        {
            _ = _loggingService.LogDebugAsync($"Subtask started: {e.Subtask.Name} for {e.QueueItem.DisplayName}");

            // Update queue item status message
            e.QueueItem.StatusMessage = e.Subtask.StatusMessage;
        }

        private void OnTaskExecutorSubtaskCompleted(object? sender, AutoLoginSubtaskEventArgs e)
        {
            _ = _loggingService.LogDebugAsync($"Subtask completed: {e.Subtask.Name} for {e.QueueItem.DisplayName}");
        }

        private void OnTaskExecutorSubtaskFailed(object? sender, AutoLoginSubtaskEventArgs e)
        {
            _ = _loggingService.LogWarningAsync($"Subtask failed: {e.Subtask.Name} for {e.QueueItem.DisplayName} - {e.Message}");
        }

        private void OnTaskExecutorSubtaskProgressUpdated(object? sender, AutoLoginSubtaskEventArgs e)
        {
            _ = _loggingService.LogDebugAsync($"Subtask progress: {e.Subtask.Name} - {e.Subtask.Progress}% for {e.QueueItem.DisplayName}");

            // Notify UI of progress update
            ItemProgressUpdated?.Invoke(this, new AutoLoginQueueItemEventArgs(e.QueueItem, $"{e.Subtask.Name}: {e.Subtask.Progress}%"));
        }

        #endregion

        #region Helper Methods

        private void LoadConfigurationFromPersistenceService()
        {
            // Configuration is now loaded by the individual services
            // This method exists for backward compatibility
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            // Clean up task executor event handlers
            CleanupTaskExecutorEventHandlers();

            // Dispose orchestrator (which handles cancellation and cleanup)
            if (_executionOrchestrator is IDisposable disposableOrchestrator)
            {
                disposableOrchestrator.Dispose();
            }

            // Task executor disposal handled by DI container

            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
