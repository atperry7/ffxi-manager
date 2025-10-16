using System;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Services.AutoLogin;

namespace FFXIManager.Services
{
    /// <summary>
    /// Implementation of auto-login task executor that handles the actual login steps.
    /// Separates execution logic from queue management for better testability and maintainability.
    /// </summary>
    public class AutoLoginTaskExecutor : IAutoLoginTaskExecutor, IDisposable
    {
        private readonly ILoggingService _loggingService;
        private readonly ILoginTaskHandlerResolver _handlerResolver;
        private readonly IAutoLoginContextService _contextService;
        private readonly IWorkflowService _workflowService;
        private readonly WorkflowTaskBuilder _workflowTaskBuilder;
        private readonly object _lockObject = new();
        private CancellationTokenSource? _currentTaskCancellationTokenSource;
        private AutoLoginQueueItem? _currentQueueItem;
        private AutoLoginTask? _currentTask;
        private bool _disposed;

        public AutoLoginTaskExecutor(
            ILoggingService loggingService,
            ILoginTaskHandlerResolver handlerResolver,
            IAutoLoginContextService contextService,
            IWorkflowService workflowService,
            WorkflowTaskBuilder workflowTaskBuilder)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _handlerResolver = handlerResolver ?? throw new ArgumentNullException(nameof(handlerResolver));
            _contextService = contextService ?? throw new ArgumentNullException(nameof(contextService));
            _workflowService = workflowService ?? throw new ArgumentNullException(nameof(workflowService));
            _workflowTaskBuilder = workflowTaskBuilder ?? throw new ArgumentNullException(nameof(workflowTaskBuilder));

            // Default configuration - increased to accommodate extended detection operations
            SubtaskTimeoutSeconds = 90; // Increased from 30s to allow for Extended (60s) + buffer
            DelayBetweenSubtasks = 1000;
            ContinueOnSubtaskFailure = false;
        }

        #region Properties

        public int SubtaskTimeoutSeconds { get; set; }
        public int DelayBetweenSubtasks { get; set; }
        public bool ContinueOnSubtaskFailure { get; set; }

        #endregion

        #region Events

        public event EventHandler<AutoLoginTaskEventArgs>? TaskStarted;
        public event EventHandler<AutoLoginTaskEventArgs>? TaskCompleted;
        public event EventHandler<AutoLoginTaskEventArgs>? TaskFailed;
        public event EventHandler<AutoLoginTaskEventArgs>? TaskProgressUpdated;
        public event EventHandler<AutoLoginSubtaskEventArgs>? SubtaskStarted;
        public event EventHandler<AutoLoginSubtaskEventArgs>? SubtaskCompleted;
        public event EventHandler<AutoLoginSubtaskEventArgs>? SubtaskFailed;
        public event EventHandler<AutoLoginSubtaskEventArgs>? SubtaskProgressUpdated;

        #endregion

        #region Execution

        /// <summary>
        /// Executes an auto-login task for the specified queue item
        /// </summary>
        public async Task ExecuteAsync(AutoLoginQueueItem queueItem, CancellationToken cancellationToken = default)
        {
            if (queueItem == null) throw new ArgumentNullException(nameof(queueItem));

            lock (_lockObject)
            {
                if (_currentQueueItem != null)
                {
                    throw new InvalidOperationException("Another task is already executing");
                }

                _currentQueueItem = queueItem;
                _currentTaskCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            }

            try
            {
                await ExecuteTaskInternalAsync(queueItem, _currentTaskCancellationTokenSource.Token);
            }
            finally
            {
                lock (_lockObject)
                {
                    _currentQueueItem = null;
                    _currentTask = null;
                    _currentTaskCancellationTokenSource?.Dispose();
                    _currentTaskCancellationTokenSource = null;
                }
            }
        }

        /// <summary>
        /// Internal task execution with proper error handling and progress reporting
        /// </summary>
        private async Task ExecuteTaskInternalAsync(AutoLoginQueueItem queueItem, CancellationToken cancellationToken)
        {
            // Create or get the task
            var task = queueItem.Task ?? await CreateWorkflowDrivenTaskAsync(queueItem, cancellationToken);
            queueItem.Task = task;
            _currentTask = task;

            _ = _loggingService.LogInfoAsync($"Starting auto-login task execution for {queueItem.DisplayName}");

            try
            {
                // Start the task
                task.Start();
                OnTaskStarted(new AutoLoginTaskEventArgs(queueItem, task, "Task execution started"));

                // Execute each subtask
                foreach (var subtask in task.Subtasks)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Update current subtask
                    task.CurrentSubtask = subtask;
                    OnTaskProgressUpdated(new AutoLoginTaskEventArgs(queueItem, task, $"Starting subtask: {subtask.Name}"));

                    // Execute the subtask
                    await ExecuteSubtaskAsync(queueItem, task, subtask, cancellationToken);

                    // Check if we should continue on failure
                    if (subtask.Status == AutoLoginSubtaskStatus.Failed && !ContinueOnSubtaskFailure)
                    {
                        task.Fail($"Subtask failed: {subtask.Name} - {subtask.ErrorMessage}");
                        OnTaskFailed(new AutoLoginTaskEventArgs(queueItem, task, task.ErrorMessage));

                        // Clean up context when task fails due to subtask failure
                        await _contextService.DisposeContextAsync(queueItem.Id.ToString());
                        return;
                    }

                    // Add delay between subtasks if configured
                    if (DelayBetweenSubtasks > 0 && subtask != task.Subtasks[^1]) // Not the last subtask
                    {
                        await Task.Delay(DelayBetweenSubtasks, cancellationToken);
                    }
                }

                // Mark task as completed
                task.Complete();
                OnTaskCompleted(new AutoLoginTaskEventArgs(queueItem, task, "Task execution completed successfully"));
                _ = _loggingService.LogInfoAsync($"Auto-login task completed successfully for {queueItem.DisplayName}");

                // Clean up context
                await _contextService.DisposeContextAsync(queueItem.Id.ToString());
            }
            catch (OperationCanceledException)
            {
                task.Cancel();
                _ = _loggingService.LogInfoAsync($"Auto-login task cancelled for {queueItem.DisplayName}");

                // Clean up context
                await _contextService.DisposeContextAsync(queueItem.Id.ToString());
                throw;
            }
            catch (Exception ex)
            {
                var errorMessage = $"Unexpected error during task execution: {ex.Message}";
                task.Fail(errorMessage);
                OnTaskFailed(new AutoLoginTaskEventArgs(queueItem, task, errorMessage));
                _ = _loggingService.LogErrorAsync($"Auto-login task failed for {queueItem.DisplayName}", ex);

                // Clean up context
                await _contextService.DisposeContextAsync(queueItem.Id.ToString());
                throw;
            }
        }

        /// <summary>
        /// Executes a single subtask with timeout and progress reporting
        /// </summary>
        private async Task ExecuteSubtaskAsync(AutoLoginQueueItem queueItem, AutoLoginTask task, AutoLoginSubtask subtask, CancellationToken cancellationToken)
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(SubtaskTimeoutSeconds));
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                _ = _loggingService.LogDebugAsync($"Starting subtask: {subtask.Name} for {queueItem.DisplayName}");

                // Start the subtask
                subtask.Start();
                OnSubtaskStarted(new AutoLoginSubtaskEventArgs(queueItem, task, subtask, "Subtask started"));

                // Execute subtask using appropriate handler
                await ExecuteSubtaskWithHandlerAsync(queueItem, task, subtask, combinedCts.Token);

                // Complete the subtask only if it's not already in a terminal state
                // Handlers may call subtask.Fail(), subtask.Skip(), etc. which should not be overridden
                if (subtask.Status == AutoLoginSubtaskStatus.InProgress)
                {
                    subtask.Complete();
                    OnSubtaskCompleted(new AutoLoginSubtaskEventArgs(queueItem, task, subtask, "Subtask completed"));
                    _ = _loggingService.LogDebugAsync($"Completed subtask: {subtask.Name} for {queueItem.DisplayName}");
                }
                else
                {
                    // Subtask was already completed by handler (failed, skipped, etc.)
                    _ = _loggingService.LogDebugAsync($"Subtask {subtask.Name} finished with status: {subtask.Status} for {queueItem.DisplayName}");
                }
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
            {
                var errorMessage = $"Subtask timed out after {SubtaskTimeoutSeconds} seconds";
                subtask.Fail(errorMessage);
                OnSubtaskFailed(new AutoLoginSubtaskEventArgs(queueItem, task, subtask, errorMessage));
                _ = _loggingService.LogWarningAsync($"Subtask {subtask.Name} timed out for {queueItem.DisplayName}");

                if (!ContinueOnSubtaskFailure)
                {
                    throw new TimeoutException(errorMessage);
                }
            }
            catch (OperationCanceledException)
            {
                subtask.Cancel();
                _ = _loggingService.LogDebugAsync($"Subtask {subtask.Name} cancelled for {queueItem.DisplayName}");
                throw;
            }
            catch (Exception ex)
            {
                var errorMessage = $"Subtask execution failed: {ex.Message}";
                subtask.Fail(errorMessage);
                OnSubtaskFailed(new AutoLoginSubtaskEventArgs(queueItem, task, subtask, errorMessage));
                _ = _loggingService.LogErrorAsync($"Subtask {subtask.Name} failed for {queueItem.DisplayName}", ex);

                if (!ContinueOnSubtaskFailure)
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// Executes a subtask using the appropriate handler from the resolver.
        /// Includes pause state monitoring and progress reporting integration.
        /// </summary>
        private async Task ExecuteSubtaskWithHandlerAsync(AutoLoginQueueItem queueItem, AutoLoginTask task, AutoLoginSubtask subtask, CancellationToken cancellationToken)
        {
            // Get the appropriate handler for this subtask
            var handler = _handlerResolver.GetHandler(subtask);
            if (handler == null)
            {
                throw new InvalidOperationException($"No handler registered for subtask: {subtask.TaskStep}. Please ensure all task steps have corresponding handlers registered in DependencyInjection.");
            }

            _ = _loggingService.LogDebugAsync($"Executing subtask {subtask.TaskStep} using handler: {handler.GetType().Name} for {queueItem.DisplayName}");

            // Set up progress monitoring that respects pause state
            var lastProgress = 0;
            subtask.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(AutoLoginSubtask.Progress) && subtask.Progress != lastProgress)
                {
                    lastProgress = subtask.Progress;
                    OnSubtaskProgressUpdated(new AutoLoginSubtaskEventArgs(queueItem, task, subtask, $"Progress: {subtask.Progress}%"));
                    OnTaskProgressUpdated(new AutoLoginTaskEventArgs(queueItem, task, $"Task progress: {task.Progress}%"));
                }
            };

            // Execute using the handler with pause state monitoring
            while (!cancellationToken.IsCancellationRequested)
            {
                // Check for pause state and wait if paused
                while (task.Status == AutoLoginTaskStatus.Paused && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(100, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Get context for this queue item and execute the actual handler logic
                    var context = _contextService.GetContext(queueItem.Id.ToString());
                    await handler.ExecuteAsync(subtask, queueItem, context, cancellationToken);
                    break; // Success, exit loop
                }
                catch (OperationCanceledException)
                {
                    // Re-throw cancellation to be handled at higher level
                    throw;
                }
                catch (Exception ex)
                {
                    // Handler threw an exception, let it bubble up
                    _ = _loggingService.LogErrorAsync($"Handler {handler.GetType().Name} failed for subtask {subtask.TaskStep}", ex);
                    throw;
                }
            }
        }


        /// <summary>
        /// Creates a workflow-driven auto-login task with subtasks built from the workflow definition.
        /// This is the new approach that replaces the obsolete CreateStandardLoginTask method.
        /// </summary>
        private async Task<AutoLoginTask> CreateWorkflowDrivenTaskAsync(AutoLoginQueueItem queueItem, CancellationToken cancellationToken)
        {
            var task = new AutoLoginTask
            {
                Name = "FFXI Auto-Login",
                Description = "Complete auto-login sequence for Final Fantasy XI"
            };

            // Load the workflow for this account (falls back to default workflow)
            var workflow = await _workflowService.GetWorkflowForAccountAsync(queueItem.Account, cancellationToken);

            await _loggingService.LogInfoAsync($"Using workflow '{workflow.Name}' for {queueItem.DisplayName}");

            // Build subtasks from the workflow
            var subtasks = await _workflowTaskBuilder.BuildSubtasksAsync(workflow, queueItem.Account, cancellationToken);

            await _loggingService.LogInfoAsync($"Built {subtasks.Count} subtasks from workflow '{workflow.Name}' for {queueItem.DisplayName}");

            // Add workflow-generated subtasks to the task
            foreach (var subtask in subtasks)
            {
                task.AddSubtask(subtask);
            }

            return task;
        }

        #endregion

        #region Control Methods

        /// <summary>
        /// Pauses the current task execution
        /// </summary>
        public Task<bool> PauseCurrentTaskAsync()
        {
            string? taskName = null;
            bool paused = false;

            lock (_lockObject)
            {
                if (_currentTask?.Status == AutoLoginTaskStatus.InProgress)
                {
                    _currentTask.Pause();
                    taskName = _currentTask.Name;
                    paused = true;
                }
            }

            if (paused && taskName != null)
            {
                _ = _loggingService.LogInfoAsync($"Paused current task: {taskName}");
            }

            return Task.FromResult(paused);
        }

        /// <summary>
        /// Resumes a paused task execution
        /// </summary>
        public Task<bool> ResumeCurrentTaskAsync()
        {
            string? taskName = null;
            bool resumed = false;

            lock (_lockObject)
            {
                if (_currentTask?.Status == AutoLoginTaskStatus.Paused)
                {
                    _currentTask.Resume();
                    taskName = _currentTask.Name;
                    resumed = true;
                }
            }

            if (resumed && taskName != null)
            {
                _ = _loggingService.LogInfoAsync($"Resumed current task: {taskName}");
            }

            return Task.FromResult(resumed);
        }

        /// <summary>
        /// Skips the current subtask
        /// </summary>
        public Task<bool> SkipCurrentSubtaskAsync()
        {
            string? subtaskName = null;
            bool skipped = false;

            lock (_lockObject)
            {
                if (_currentTask?.CurrentSubtask?.Status == AutoLoginSubtaskStatus.InProgress)
                {
                    var subtask = _currentTask.CurrentSubtask;
                    subtask.Skip("User requested skip");

                    if (_currentQueueItem != null)
                    {
                        OnSubtaskCompleted(new AutoLoginSubtaskEventArgs(_currentQueueItem, _currentTask, subtask, "Subtask skipped"));
                    }

                    subtaskName = subtask.Name;
                    skipped = true;
                }
            }

            if (skipped && subtaskName != null)
            {
                _ = _loggingService.LogInfoAsync($"Skipped current subtask: {subtaskName}");
            }

            return Task.FromResult(skipped);
        }

        #endregion

        #region Event Raising

        protected virtual void OnTaskStarted(AutoLoginTaskEventArgs e) => TaskStarted?.Invoke(this, e);
        protected virtual void OnTaskCompleted(AutoLoginTaskEventArgs e) => TaskCompleted?.Invoke(this, e);
        protected virtual void OnTaskFailed(AutoLoginTaskEventArgs e) => TaskFailed?.Invoke(this, e);
        protected virtual void OnTaskProgressUpdated(AutoLoginTaskEventArgs e) => TaskProgressUpdated?.Invoke(this, e);
        protected virtual void OnSubtaskStarted(AutoLoginSubtaskEventArgs e) => SubtaskStarted?.Invoke(this, e);
        protected virtual void OnSubtaskCompleted(AutoLoginSubtaskEventArgs e) => SubtaskCompleted?.Invoke(this, e);
        protected virtual void OnSubtaskFailed(AutoLoginSubtaskEventArgs e) => SubtaskFailed?.Invoke(this, e);
        protected virtual void OnSubtaskProgressUpdated(AutoLoginSubtaskEventArgs e) => SubtaskProgressUpdated?.Invoke(this, e);

        #endregion

        #region Disposal

        public void Dispose()
        {
            if (_disposed) return;

            lock (_lockObject)
            {
                _currentTaskCancellationTokenSource?.Cancel();
                _currentTaskCancellationTokenSource?.Dispose();
                _currentTask?.Cleanup();
            }

            _disposed = true;
        }

        #endregion
    }
}