using FFXIManager.Models;

namespace FFXIManager.Services.AutoLogin.ActionExecutors
{
    /// <summary>
    /// Executes "Wait" actions to introduce deliberate delays in workflow execution.
    /// Useful for waiting after slow operations like screen transitions or application launches.
    /// </summary>
    /// <remarks>
    /// Wait actions are passive - they don't interact with any UI or processes.
    /// The delay duration is specified via the DelayMs property on KeyboardAction.
    ///
    /// **Use Cases:**
    /// - Wait for screen transitions to complete
    /// - Allow time for animations or UI rendering
    /// - Delay after launching applications
    /// - Rate-limiting to avoid overwhelming target application
    /// </remarks>
    public class WaitActionExecutor : BaseWorkflowActionExecutor
    {
        public override string ActionType => "Wait";

        public WaitActionExecutor(ILoggingService loggingService)
            : base(loggingService)
        {
        }

        protected override async Task<bool> ExecuteActionAsync(
            KeyboardAction action,
            WorkflowActionContext context,
            CancellationToken cancellationToken)
        {
            var delayMs = Math.Max(0, action.DelayMs);

            if (delayMs == 0)
            {
                await _loggingService.LogDebugAsync("[WAIT] No delay specified (DelayMs = 0)");
                return true;
            }

            await _loggingService.LogDebugAsync($"[WAIT] Waiting {delayMs}ms");

            await Task.Delay(delayMs, cancellationToken);

            await _loggingService.LogDebugAsync("[WAIT] Wait completed");

            return true;
        }
    }
}
