using FFXIManager.Models;
using FFXIManager.Services.AutoLogin.ScreenDetection;
using System.Drawing;

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
        private readonly IScreenDetectionCoordinator _screenDetection;

        public override string ActionType => "Click";

        public override bool RequiresWindowHandle => true;

        public ClickActionExecutor(
            ILoggingService loggingService,
            IUIAutomationService automationService,
            IScreenDetectionCoordinator screenDetection)
            : base(loggingService)
        {
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
            _screenDetection = screenDetection ?? throw new ArgumentNullException(nameof(screenDetection));
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

            // Determine if multiple click points are defined via Parameters["ClickPoints"] (JSON)
            var multiPoints = action.GetParameter<System.Collections.Generic.List<RelativeClickOffset>>("ClickPoints", new List<RelativeClickOffset>());
            if (multiPoints == null || multiPoints.Count == 0)
            {
                await _loggingService.LogErrorAsync("[CLICK] No click points configured. Add at least 1 point to Parameters['ClickPoints'].");
                return false;
            }

            // Ensure fresh handle in case of splash → main transitions
            if (!await context.EnsureFreshWindowHandleAsync())
            {
                await _loggingService.LogErrorAsync("[CLICK] Unable to refresh window handle before capture");
                return false;
            }

            // Capture screenshot to convert to screen coordinates (with logging + retries)
            var screenshot = await _screenDetection.CaptureScreenshotWithLogging(context.WindowHandle, "click coordinate conversion", cancellationToken, retryCount: 0);
            if (screenshot == null || !screenshot.IsValid)
            {
                await _loggingService.LogErrorAsync("[CLICK] Failed to capture window screenshot for coordinate conversion");
                return false;
            }

            // Diagnostic logging for coordinate debugging
            await _loggingService.LogInfoAsync($"[CLICK_DEBUG] Screenshot dimensions: {screenshot.Width}x{screenshot.Height}");
            await _loggingService.LogInfoAsync($"[CLICK_DEBUG] Window bounds: {screenshot.WindowBounds} (size: {screenshot.WindowBounds.Width}x{screenshot.WindowBounds.Height})");
            await _loggingService.LogInfoAsync($"[CLICK_DEBUG] DPI scale: {screenshot.DpiScale}");
            await _loggingService.LogInfoAsync($"[CLICK_DEBUG] Template match position: {context.TemplateMatch.WindowRelativePosition}");
            await _loggingService.LogInfoAsync($"[CLICK_DEBUG] Template match size: {context.TemplateMatch.MatchSize}");

            // Activate window first
            await _automationService.EnsureWindowFocusAsync(context.WindowHandle, cancellationToken);
            await Task.Delay(100, cancellationToken);

            await _loggingService.LogInfoAsync($"[CLICK] Executing multi-click sequence with {multiPoints!.Count} point(s)");
            for (int i = 0; i < multiPoints.Count; i++)
            {
                var p = multiPoints[i];
                var absolute = CalculateRelativeClickPoint(context.TemplateMatch, p.X, p.Y);
                await _loggingService.LogInfoAsync($"[CLICK_DEBUG] Point {i + 1}: relative offset=({p.X}, {p.Y})");
                await _loggingService.LogInfoAsync($"[CLICK_DEBUG] Point {i + 1}: absolute window-relative=({absolute.X}, {absolute.Y})");

                var screenPoint = screenshot.ToScreenCoordinates(absolute);
                await _loggingService.LogInfoAsync($"[CLICK_DEBUG] Point {i + 1}: final screen coordinates=({screenPoint.X}, {screenPoint.Y})");

                if (!IsReasonable(screenPoint))
                {
                    await _loggingService.LogWarningAsync($"[CLICK] Skipping out-of-bounds point {i + 1}: ({screenPoint.X}, {screenPoint.Y})");
                    continue;
                }

                await _loggingService.LogDebugAsync($"[CLICK] Point {i + 1}: screen=({screenPoint.X}, {screenPoint.Y})");
                await _automationService.MoveMouseAsync(screenPoint, cancellationToken);
                await Task.Delay(150, cancellationToken);
                await _automationService.ClickAsync(screenPoint, cancellationToken);
                if (i < multiPoints.Count - 1 && action.DelayMs > 0)
                {
                    await Task.Delay(action.DelayMs, cancellationToken);
                }
            }

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

        private static bool IsReasonable(Point screenPoint)
            => screenPoint.X >= 0 && screenPoint.Y >= 0 && screenPoint.X <= 8000 && screenPoint.Y <= 8000;

        // Multi-point list is directly stored in action.Parameters["ClickPoints"]
    }
}
