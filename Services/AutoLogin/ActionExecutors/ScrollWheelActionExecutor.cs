using FFXIManager.Models;
using FFXIManager.Services.AutoLogin.ScreenDetection;
using System.Drawing;

namespace FFXIManager.Services.AutoLogin.ActionExecutors
{
    /// <summary>
    /// Executes mouse scroll wheel actions at a specified position.
    /// Supports both template-relative and window-center-relative positioning.
    /// </summary>
    /// <remarks>
    /// **Generic Reusable Action:**
    /// This executor is designed to be composable - it handles ONLY scrolling behavior.
    /// Combine with other actions (Click, MemberSlot, etc.) to build complete workflows.
    ///
    /// **Parameters:**
    /// - Direction (string): "Up" or "Down" (case-insensitive)
    /// - Ticks (int): Number of scroll wheel ticks (positive values only, direction handled separately)
    /// - ScrollDelayMs (int, optional): Delay between scroll ticks (default: 50ms)
    /// - PositionMouse (RelativeClickOffset, optional): Where to position mouse before scrolling
    ///   - If omitted, positions at window center (ideal for POL member selection)
    ///   - Supports FromCenter=true for resolution-independent positioning
    ///   - Supports FromCenter=false for template-relative positioning
    ///
    /// **Use Cases:**
    /// - Reset POL member selection to top (scroll up aggressively)
    /// - Navigate FFXI character lists
    /// - Scroll through any list-based UI
    ///
    /// **Example Workflow Sequence:**
    /// ```json
    /// [
    ///   {
    ///     "Action": "ScrollWheel",
    ///     "Description": "Reset to top",
    ///     "Parameters": {
    ///       "Direction": "Up",
    ///       "Ticks": 30,
    ///       "ScrollDelayMs": 50,
    ///       "PositionMouse": {"FromCenter": true, "X": 0.0, "Y": 0.0}
    ///     }
    ///   },
    ///   {
    ///     "Action": "MemberSlot",
    ///     "Description": "Select target slot"
    ///   }
    /// ]
    /// ```
    /// </remarks>
    public class ScrollWheelActionExecutor : BaseWorkflowActionExecutor
    {
        private readonly IUIAutomationService _automationService;

        public override string ActionType => "ScrollWheel";
        public override bool RequiresWindowHandle => true;

        public ScrollWheelActionExecutor(
            ILoggingService loggingService,
            IUIAutomationService automationService)
            : base(loggingService)
        {
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
        }

        protected override async Task<bool> ExecuteActionAsync(
            KeyboardAction action,
            WorkflowActionContext context,
            CancellationToken cancellationToken)
        {
            // Validate window handle
            if (context.WindowHandle == IntPtr.Zero)
            {
                await _loggingService.LogErrorAsync("[SCROLL-WHEEL] Window handle is required");
                return false;
            }

            // Get parameters
            var direction = action.GetParameter<string>("Direction", "Up");
            var ticks = action.GetParameter<int>("Ticks", 1);
            var scrollDelayMs = action.GetParameter<int>("ScrollDelayMs", 50);
            var positionMouse = action.GetParameter<RelativeClickOffset?>("PositionMouse", null);

            // Validate direction
            var isUp = direction.Equals("Up", StringComparison.OrdinalIgnoreCase);
            var isDown = direction.Equals("Down", StringComparison.OrdinalIgnoreCase);

            if (!isUp && !isDown)
            {
                await _loggingService.LogErrorAsync($"[SCROLL-WHEEL] Invalid Direction: '{direction}' (must be 'Up' or 'Down')");
                return false;
            }

            if (ticks <= 0)
            {
                await _loggingService.LogWarningAsync($"[SCROLL-WHEEL] Ticks must be positive (got {ticks}), skipping");
                return true;
            }

            await _loggingService.LogDebugAsync($"[SCROLL-WHEEL] Scrolling {direction} ×{ticks} (delay: {scrollDelayMs}ms)");

            // Ensure window focus
            await _automationService.EnsureWindowFocusAsync(context.WindowHandle, cancellationToken);
            await Task.Delay(100, cancellationToken);

            // Position mouse (default: window center)
            Point mousePosition;

            if (positionMouse != null)
            {
                mousePosition = CalculateMousePosition(positionMouse, context);
                await _loggingService.LogDebugAsync($"[SCROLL-WHEEL] Positioning mouse at ({mousePosition.X}, {mousePosition.Y}) - {(positionMouse.FromCenter ? "center-relative" : "template-relative")}");
            }
            else
            {
                // Default: window center
                mousePosition = _automationService.GetWindowCenter(context.WindowHandle);
                await _loggingService.LogDebugAsync($"[SCROLL-WHEEL] Positioning mouse at window center ({mousePosition.X}, {mousePosition.Y})");
            }

            if (mousePosition.IsEmpty)
            {
                await _loggingService.LogErrorAsync("[SCROLL-WHEEL] Failed to calculate mouse position");
                return false;
            }

            await _automationService.MoveMouseAsync(mousePosition, cancellationToken);
            await Task.Delay(100, cancellationToken);

            // Execute scroll ticks
            var delta = isUp ? 1 : -1;

            for (int i = 0; i < ticks; i++)
            {
                await _automationService.ScrollMouseWheelAsync(delta, cancellationToken);

                if (i < ticks - 1) // Don't delay after last tick
                {
                    await Task.Delay(scrollDelayMs, cancellationToken);
                }
            }

            await _loggingService.LogDebugAsync($"[SCROLL-WHEEL] Completed {ticks} scroll ticks {direction}");

            return true;
        }

        /// <summary>
        /// Calculates the mouse position using either center-relative or template-relative coordinates
        /// </summary>
        private Point CalculateMousePosition(RelativeClickOffset offset, WorkflowActionContext context)
        {
            if (offset.FromCenter)
            {
                // Center-relative calculation (template-independent, resolution-independent)
                var centerPoint = _automationService.GetWindowCenter(context.WindowHandle);
                var windowRect = _automationService.GetWindowClientRect(context.WindowHandle);

                // Convert percentage offsets to pixel offsets from center
                var offsetX = (int)(offset.X * windowRect.Width);
                var offsetY = (int)(offset.Y * windowRect.Height);

                // Calculate final screen position
                var screenX = centerPoint.X + offsetX;
                var screenY = centerPoint.Y + offsetY;

                return new Point(screenX, screenY);
            }
            else
            {
                // Template-relative calculation
                if (context.TemplateMatch == null)
                {
                    _loggingService.LogErrorAsync("[SCROLL-WHEEL] Template-relative position requested but no template match available");
                    return Point.Empty;
                }

                var rect = context.TemplateMatch.GetBoundingRectangle();
                var wx = rect.Left + (int)Math.Round(offset.X * rect.Width);
                var wy = rect.Top + (int)Math.Round(offset.Y * rect.Height);

                return new Point(wx, wy);
            }
        }
    }
}
