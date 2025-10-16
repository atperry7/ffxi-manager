using System;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Base class for workflow action executors providing common functionality
    /// </summary>
    public abstract class BaseWorkflowActionExecutor : IWorkflowActionExecutor
    {
        protected readonly ILoggingService _loggingService;

        protected BaseWorkflowActionExecutor(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        /// <summary>
        /// The action type this executor handles
        /// </summary>
        public abstract string ActionType { get; }

        /// <summary>
        /// Executes the workflow action with error handling and logging
        /// </summary>
        public async Task<bool> ExecuteAsync(
            KeyboardAction action,
            WorkflowActionContext context,
            CancellationToken cancellationToken)
        {
            try
            {
                await _loggingService.LogDebugAsync($"[ACTION-EXECUTOR] Executing {ActionType} action");

                var result = await ExecuteActionAsync(action, context, cancellationToken);

                await _loggingService.LogDebugAsync($"[ACTION-EXECUTOR] {ActionType} action completed: {result}");

                return result;
            }
            catch (OperationCanceledException)
            {
                await _loggingService.LogInfoAsync($"[ACTION-EXECUTOR] {ActionType} action cancelled");
                throw;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"[ACTION-EXECUTOR] {ActionType} action failed", ex);
                return false;
            }
        }

        /// <summary>
        /// Derived classes implement the actual action execution logic
        /// </summary>
        protected abstract Task<bool> ExecuteActionAsync(
            KeyboardAction action,
            WorkflowActionContext context,
            CancellationToken cancellationToken);

        /// <summary>
        /// Helper to update progress on the current subtask
        /// </summary>
        protected async Task UpdateProgressAsync(WorkflowActionContext context, int progress, string message)
        {
            if (context.Subtask != null)
            {
                context.Subtask.UpdateProgress(progress, message);
                await _loggingService.LogDebugAsync($"[PROGRESS] {message} ({progress}%)");
            }
        }
    }
}
