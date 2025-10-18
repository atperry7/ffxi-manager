using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Services.AutoLogin.ScreenDetection;

namespace FFXIManager.Services.AutoLogin.ActionExecutors
{
    /// <summary>
    /// Executes mouse click actions using relative coordinates.
    /// Clicks are resolution-independent, calculated relative to detected template regions.
    /// </summary>
    /// <remarks>
    /// **Click Coordinate System:**
    /// - ClickX/ClickY are relative percentages (0.0 to 1.0)
    /// - 0.5, 0.5 = center of template region
    /// - 0.0, 0.0 = top-left corner
    /// - 1.0, 1.0 = bottom-right corner
    ///
    /// **Prerequisites:**
    /// - context.TemplateMatch must be non-null (from prior detection)
    /// - Window must be visible and valid
    /// </remarks>
    public class ClickActionExecutor : BaseWorkflowActionExecutor
    {
        private readonly IUIAutomationService _automationService;
        private readonly IScreenshotCaptureService _screenshotService;

        public override string ActionType => "Click";

        public override bool RequiresWindowHandle => true;

        public ClickActionExecutor(
            ILoggingService loggingService,
            IUIAutomationService automationService,
            IScreenshotCaptureService screenshotService)
            : base(loggingService)
        {
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
            _screenshotService = screenshotService ?? throw new ArgumentNullException(nameof(screenshotService));
        }

        protected override async Task<bool> ExecuteActionAsync(
            KeyboardAction action,
            WorkflowActionContext context,
            CancellationToken cancellationToken)
        {
            // Validate prerequisites
            if (context.WindowHandle == IntPtr.Zero)
            {
                await _loggingService.LogErrorAsync("[CLICK] Window handle is required for click actions");
                return false;
            }

            if (context.TemplateMatch == null)
            {
                await _loggingService.LogWarningAsync("[CLICK] No template match available - click actions require prior screen detection");
                return false;
            }

            // Extract click coordinates (default to center)
            var clickX = action.ClickX;
            var clickY = action.ClickY;

            await _loggingService.LogDebugAsync($"[CLICK] Calculating click point: relative ({clickX:F2}, {clickY:F2})");

            // Calculate absolute window-relative point from template match and relative offset
            var absolutePoint = CalculateRelativeClickPoint(context.TemplateMatch, clickX, clickY);

            await _loggingService.LogDebugAsync($"[CLICK] Absolute window-relative point: ({absolutePoint.X}, {absolutePoint.Y})");

            // Capture screenshot to convert to screen coordinates
            var screenshot = await _screenshotService.CaptureWindowAsync(context.WindowHandle, cancellationToken);
            if (screenshot == null || !screenshot.IsValid)
            {
                await _loggingService.LogErrorAsync("[CLICK] Failed to capture window screenshot for coordinate conversion");
                return false;
            }

            var screenPoint = screenshot.ToScreenCoordinates(absolutePoint);

            // Validate screen coordinates are reasonable
            if (screenPoint.X < 0 || screenPoint.Y < 0 || screenPoint.X > 3840 || screenPoint.Y > 2160)
            {
                await _loggingService.LogWarningAsync($"[CLICK] Screen coordinates outside reasonable bounds: ({screenPoint.X}, {screenPoint.Y})");
                return false;
            }

            await _loggingService.LogInfoAsync($"[CLICK] Clicking at screen coordinates: ({screenPoint.X}, {screenPoint.Y})");

            // Activate window first
            await _automationService.EnsureWindowFocusAsync(context.WindowHandle, cancellationToken);
            await Task.Delay(100, cancellationToken);

            // Move mouse for visual feedback
            await _automationService.MoveMouseAsync(screenPoint, cancellationToken);
            await Task.Delay(200, cancellationToken);

            // Perform click
            await _automationService.ClickAsync(screenPoint, cancellationToken);

            // Post-click delay from action configuration
            if (action.DelayMs > 0)
            {
                await Task.Delay(action.DelayMs, cancellationToken);
            }

            await _loggingService.LogDebugAsync("[CLICK] Click completed successfully");

            return true;
        }

        /// <summary>
        /// Calculates absolute window-relative click point from template match and relative offset
        /// </summary>
        private Point CalculateRelativeClickPoint(TemplateMatchResult templateMatch, double relativeX, double relativeY)
        {
            var absoluteX = templateMatch.WindowRelativePosition.X + (int)(templateMatch.MatchSize.Width * relativeX);
            var absoluteY = templateMatch.WindowRelativePosition.Y + (int)(templateMatch.MatchSize.Height * relativeY);

            return new Point(absoluteX, absoluteY);
        }
    }
}
