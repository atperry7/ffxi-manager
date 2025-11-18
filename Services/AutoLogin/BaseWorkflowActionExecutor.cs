using FFXIManager.Models;
using FFXIManager.Services.AutoLogin.ScreenDetection;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Base class for workflow action executors providing common functionality
    /// </summary>
    public abstract class BaseWorkflowActionExecutor : IWorkflowActionExecutor
    {
        protected readonly ILoggingService _loggingService;
        protected readonly IUIAutomationService? _uiAutomationService;

        protected BaseWorkflowActionExecutor(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        protected BaseWorkflowActionExecutor(ILoggingService loggingService, IUIAutomationService uiAutomationService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _uiAutomationService = uiAutomationService ?? throw new ArgumentNullException(nameof(uiAutomationService));
        }

        /// <summary>
        /// The action type this executor handles
        /// </summary>
        public abstract string ActionType { get; }

        /// <summary>
        /// Indicates whether this executor requires a valid window handle.
        /// Default: false. Override in derived classes that need window handles.
        /// </summary>
        public virtual bool RequiresWindowHandle => false;

        /// <summary>
        /// Executes the workflow action with retry, repeat, and delay support.
        /// Implements unified timing pattern: retry on failure, repeat on success, delay after completion.
        /// </summary>
        public async Task<bool> ExecuteAsync(
            KeyboardAction action,
            WorkflowActionContext context,
            CancellationToken cancellationToken)
        {
            // Hardcoded exceptions: InputPassword/InputOTP don't retry or repeat (prevents account lockouts)
            var supportsRetry = !IsPasswordOrOtpAction(action);
            var supportsRepeat = !IsPasswordOrOtpAction(action);

            var retryAttempts = supportsRetry ? action.GetParameter("RetryAttempts", 1) : 1;
            var retryDelayMs = Math.Max(50, action.GetParameter("RetryDelayMs", 500));
            var repeatCount = supportsRepeat ? Math.Max(1, action.Count) : 1;

            for (int attempt = 1; attempt <= retryAttempts; attempt++)
            {
                try
                {
                    // Repeat loop (execute N times on success)
                    for (int repeat = 1; repeat <= repeatCount; repeat++)
                    {
                        _ = _loggingService.LogDebugAsync($"[{ActionType}] Executing (attempt {attempt}/{retryAttempts}, repeat {repeat}/{repeatCount})");

                        var result = await ExecuteActionAsync(action, context, cancellationToken);

                        if (!result)
                            throw new InvalidOperationException($"{ActionType} execution returned false");

                        // Delay between repeats (not after last repeat)
                        if (repeat < repeatCount)
                        {
                            await Task.Delay(Math.Max(50, action.DelayMs), cancellationToken);
                        }
                    }

                    // Post-action delay (after all repeats complete)
                    if (action.DelayMs > 0)
                    {
                        await Task.Delay(Math.Max(50, action.DelayMs), cancellationToken);
                    }

                    _ = _loggingService.LogDebugAsync($"[{ActionType}] Completed successfully");
                    return true; // Success - exit retry loop
                }
                catch (OperationCanceledException)
                {
                    _ = _loggingService.LogInfoAsync($"[{ActionType}] Cancelled");
                    throw;
                }
                catch (Exception ex)
                {
                    if (attempt < retryAttempts)
                    {
                        _ = _loggingService.LogWarningAsync($"[{ActionType}] Attempt {attempt}/{retryAttempts} failed: {ex.Message}. Retrying in {retryDelayMs}ms...");
                        await Task.Delay(retryDelayMs, cancellationToken);
                    }
                    else
                    {
                        _ = _loggingService.LogErrorAsync($"[{ActionType}] Failed after {retryAttempts} attempt(s)", ex);
                        return false;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Determines if an action is InputPassword or InputOTP (which should not retry or repeat).
        /// </summary>
        private bool IsPasswordOrOtpAction(KeyboardAction action)
        {
            return action.Action.Equals("InputPassword", StringComparison.OrdinalIgnoreCase) ||
                   action.Action.Equals("InputOTP", StringComparison.OrdinalIgnoreCase);
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
                context.Subtask.UpdateProgressWithPhase("action", progress, message);
                await _loggingService.LogDebugAsync($"[PROGRESS] {message} ({progress}%)");
            }
        }

        /// <summary>
        /// Calculates window-relative click point using either center-relative or template-relative coordinates.
        /// Shared method to eliminate code duplication across executors.
        /// </summary>
        /// <param name="offset">Click offset configuration (FromCenter determines calculation mode)</param>
        /// <param name="context">Workflow action context containing window handle and optional template match</param>
        /// <param name="actionName">Name of the calling action for logging purposes</param>
        /// <returns>Window-relative click point in pixels</returns>
        /// <remarks>
        /// **Center-Relative Mode (FromCenter=true):**
        /// - Template-independent, resolution-independent
        /// - Coordinates are percentages relative to window center (-0.5 to 0.5)
        /// - Works with DirectX9 POL proportional scaling
        ///
        /// **Template-Relative Mode (FromCenter=false):**
        /// - Requires context.TemplateMatch to be set
        /// - Coordinates are percentages within template region (0.0 to 1.0)
        /// </remarks>
        protected System.Drawing.Point CalculateClickPoint(
            RelativeClickOffset offset,
            WorkflowActionContext context,
            string actionName)
        {
            if (_uiAutomationService == null)
            {
                throw new InvalidOperationException($"UI Automation Service is required for click point calculation in {actionName}");
            }

            if (offset.FromCenter)
            {
                // Center-relative (template-independent, resolution-independent)
                var centerPoint = _uiAutomationService.GetWindowCenter(context.WindowHandle);
                var windowRect = _uiAutomationService.GetWindowClientRect(context.WindowHandle);

                var offsetX = (int)(offset.X * windowRect.Width);
                var offsetY = (int)(offset.Y * windowRect.Height);

                var windowRelativeX = centerPoint.X - windowRect.Left + offsetX;
                var windowRelativeY = centerPoint.Y - windowRect.Top + offsetY;

                _ = _loggingService.LogDebugAsync($"[{actionName}] Click point (center-relative): ({offset.X:F2},{offset.Y:F2}) -> window=({windowRelativeX},{windowRelativeY})");

                return new System.Drawing.Point(windowRelativeX, windowRelativeY);
            }
            else
            {
                // Template-relative (existing behavior)
                if (context.TemplateMatch == null)
                {
                    throw new InvalidOperationException($"Template match required for template-relative clicks in {actionName}");
                }

                var rect = context.TemplateMatch.GetBoundingRectangle();
                var wx = rect.Left + (int)Math.Round(offset.X * rect.Width);
                var wy = rect.Top + (int)Math.Round(offset.Y * rect.Height);

                _ = _loggingService.LogDebugAsync($"[{actionName}] Click point (template-relative): ({offset.X:F2},{offset.Y:F2}) -> window=({wx},{wy})");

                return new System.Drawing.Point(wx, wy);
            }
        }

        /// <summary>
        /// Calculates mouse position using either center-relative or template-relative coordinates.
        /// Similar to CalculateClickPoint but returns screen coordinates instead of window-relative.
        /// Used primarily by ScrollWheelActionExecutor.
        /// </summary>
        /// <param name="offset">Position offset configuration</param>
        /// <param name="context">Workflow action context</param>
        /// <param name="actionName">Name of the calling action for logging</param>
        /// <returns>Screen coordinates for mouse positioning</returns>
        protected System.Drawing.Point CalculateMousePosition(
            RelativeClickOffset offset,
            WorkflowActionContext context,
            string actionName)
        {
            if (_uiAutomationService == null)
            {
                throw new InvalidOperationException($"UI Automation Service is required for mouse position calculation in {actionName}");
            }

            if (offset.FromCenter)
            {
                // Center-relative calculation (template-independent, resolution-independent)
                var centerPoint = _uiAutomationService.GetWindowCenter(context.WindowHandle);
                var windowRect = _uiAutomationService.GetWindowClientRect(context.WindowHandle);

                // Convert percentage offsets to pixel offsets from center
                var offsetX = (int)(offset.X * windowRect.Width);
                var offsetY = (int)(offset.Y * windowRect.Height);

                // Calculate final screen position
                var screenX = centerPoint.X + offsetX;
                var screenY = centerPoint.Y + offsetY;

                return new System.Drawing.Point(screenX, screenY);
            }
            else
            {
                // Template-relative calculation
                if (context.TemplateMatch == null)
                {
                    _ = _loggingService.LogErrorAsync($"[{actionName}] Template-relative position requested but no template match available");
                    return System.Drawing.Point.Empty;
                }

                var rect = context.TemplateMatch.GetBoundingRectangle();
                var wx = rect.Left + (int)Math.Round(offset.X * rect.Width);
                var wy = rect.Top + (int)Math.Round(offset.Y * rect.Height);

                return new System.Drawing.Point(wx, wy);
            }
        }
    }
}
