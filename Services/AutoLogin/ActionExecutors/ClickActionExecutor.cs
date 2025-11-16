using FFXIManager.Models;
using FFXIManager.Services.AutoLogin.ScreenDetection;
using System.Drawing;

namespace FFXIManager.Services.AutoLogin.ActionExecutors
{
    /// <summary>
    /// Executes mouse click actions using either template-relative or window-center-relative coordinates.
    /// Supports both traditional template-based navigation and resolution-independent center-based navigation.
    /// </summary>
    /// <remarks>
    /// **Template-Relative Mode (FromCenter=false, default):**
    /// - Requires context.TemplateMatch from prior detection
    /// - Coordinates are percentages within template region (0.0 to 1.0)
    /// - Example: X=0.5, Y=0.5 clicks center of matched template
    ///
    /// **Window-Center-Relative Mode (FromCenter=true):**
    /// - No template matching required (template-independent!)
    /// - Coordinates are percentages relative to window center (-0.5 to 0.5)
    /// - Resolution-independent due to DirectX9 POL proportional scaling
    /// - Example: X=0.0, Y=-0.2 clicks 20% of window height above center
    ///
    /// **Prerequisites:**
    /// - Window handle must be valid
    /// - Template match required ONLY if FromCenter=false
    /// </remarks>
    public class ClickActionExecutor : BaseWorkflowActionExecutor
    {
        private readonly IUIAutomationService _automationService;

        public override string ActionType => "Click";

        public override bool RequiresWindowHandle => true;

        public ClickActionExecutor(
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
            // Validate prerequisites
            if (context.WindowHandle == IntPtr.Zero)
            {
                _ = _loggingService.LogErrorAsync("[CLICK] Window handle is required for click actions");
                return false;
            }

            // Get click points
            var multiPoints = action.GetParameter<System.Collections.Generic.List<RelativeClickOffset>>("ClickPoints", new List<RelativeClickOffset>());
            if (multiPoints == null || multiPoints.Count == 0)
            {
                _ = _loggingService.LogErrorAsync("[CLICK] No click points configured. Add at least 1 point to Parameters['ClickPoints'].");
                return false;
            }

            // Check if any point requires template matching
            bool requiresTemplate = multiPoints.Any(p => !p.FromCenter);
            if (requiresTemplate && context.TemplateMatch == null)
            {
                _ = _loggingService.LogWarningAsync("[CLICK] Template-relative click requested but no template match available");
                return false;
            }

            // Determine navigation mode for logging
            bool hasCenter = multiPoints.Any(p => p.FromCenter);
            bool hasTemplate = multiPoints.Any(p => !p.FromCenter);
            string mode = hasCenter && hasTemplate ? "Hybrid" : hasCenter ? "Center-Relative" : "Template-Relative";

            _ = _loggingService.LogInfoAsync($"[CLICK] Executing {mode} click sequence with {multiPoints!.Count} point(s)");

            for (int i = 0; i < multiPoints.Count; i++)
            {
                var p = multiPoints[i];
                var windowRelativePoint = CalculateClickPoint(p, context);

                string modeLabel = p.FromCenter ? "center-relative" : "template-relative";
                _ = _loggingService.LogDebugAsync($"[CLICK] Point {i + 1} ({modeLabel}): ({p.X:F2},{p.Y:F2}) -> window=({windowRelativePoint.X},{windowRelativePoint.Y})");

                await _automationService.ClickWindowRelativeAsync(context.WindowHandle, windowRelativePoint, cancellationToken);

                if (i < multiPoints.Count - 1 && action.DelayMs > 0)
                {
                    await Task.Delay(Math.Max(1, action.DelayMs), cancellationToken);
                }
            }

            await Task.Delay(Math.Max(1, action.DelayMs), cancellationToken);

            _ = _loggingService.LogDebugAsync("[CLICK] Click completed successfully");

            return true;
        }

        /// <summary>
        /// Calculates window-relative click point using either template-relative or center-relative coordinates.
        /// </summary>
        /// <remarks>
        /// DirectX9 POL Behavior: POL uses proportional scaling where UI elements maintain their
        /// relative positions from the window center regardless of window size, making center-relative
        /// coordinates perfectly stable across all resolutions.
        /// </remarks>
        private Point CalculateClickPoint(RelativeClickOffset offset, WorkflowActionContext context)
        {
            if (offset.FromCenter)
            {
                // Center-relative calculation (template-independent, resolution-independent)
                var centerPoint = _automationService.GetWindowCenter(context.WindowHandle);
                var windowRect = _automationService.GetWindowClientRect(context.WindowHandle);

                // Convert percentage offsets to pixel offsets from center
                // Range: -0.5 to 0.5 (percentage of window width/height from center)
                var offsetX = (int)(offset.X * windowRect.Width);
                var offsetY = (int)(offset.Y * windowRect.Height);

                // Calculate final window-relative position
                // Note: centerPoint is already in screen coordinates, convert to window-relative
                var windowRelativeX = centerPoint.X - windowRect.Left + offsetX;
                var windowRelativeY = centerPoint.Y - windowRect.Top + offsetY;

                return new Point(windowRelativeX, windowRelativeY);
            }
            else
            {
                // Template-relative calculation (existing behavior)
                var templateMatch = context.TemplateMatch!; // Already validated in ExecuteActionAsync
                var absoluteX = templateMatch.WindowRelativePosition.X + (int)(templateMatch.MatchSize.Width * offset.X);
                var absoluteY = templateMatch.WindowRelativePosition.Y + (int)(templateMatch.MatchSize.Height * offset.Y);

                return new Point(absoluteX, absoluteY);
            }
        }
    }
}
