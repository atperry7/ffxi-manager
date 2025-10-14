using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.Services.AutoLogin.ScreenDetection;

namespace FFXIManager.Services.AutoLogin.Navigation
{
    /// <summary>
    /// Implements relative click-based navigation strategy with resolution and DPI scaling.
    /// Click coordinates are defined as percentages (0.0 to 1.0) relative to the detected
    /// template match location, making them resolution and DPI independent.
    /// </summary>
    /// <remarks>
    /// This strategy should be used as a fallback when keyboard navigation is not possible.
    /// Requires a valid TemplateMatchResult to calculate the absolute click position.
    /// </remarks>
    public class RelativeClickNavigationStrategy : INavigationStrategy
    {
        private readonly IUIAutomationService _automationService;
        private readonly IScreenshotCaptureService _screenshotService;
        private readonly ILoggingService _loggingService;

        public string StrategyName => "Relative Click Navigation";

        public RelativeClickNavigationStrategy(
            IUIAutomationService automationService,
            IScreenshotCaptureService screenshotService,
            ILoggingService loggingService)
        {
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
            _screenshotService = screenshotService ?? throw new ArgumentNullException(nameof(screenshotService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        public async Task<bool> ExecuteAsync(
            IntPtr windowHandle,
            NavigationAction action,
            TemplateMatchResult? templateMatch,
            CancellationToken cancellationToken)
        {
            if (action.ClickOffset == null)
            {
                await _loggingService.LogWarningAsync($"[{StrategyName}] No click offset defined in navigation action");
                return false;
            }

            if (templateMatch == null || !templateMatch.IsValid)
            {
                await _loggingService.LogWarningAsync($"[{StrategyName}] Template match is required for relative click navigation");
                return false;
            }

            try
            {
                // Calculate absolute click point from relative offset
                var clickPoint = CalculateAbsoluteClickPoint(templateMatch, action.ClickOffset);

                await _loggingService.LogInfoAsync(
                    $"[{StrategyName}] Clicking at relative offset ({action.ClickOffset.X:F2}, {action.ClickOffset.Y:F2}) " +
                    $"= absolute point ({clickPoint.X}, {clickPoint.Y})");

                // Capture screenshot to convert window coordinates to screen coordinates
                var screenshot = await _screenshotService.CaptureWindowAsync(windowHandle, cancellationToken);
                if (screenshot == null || !screenshot.IsValid)
                {
                    await _loggingService.LogErrorAsync($"[{StrategyName}] Failed to capture window for coordinate conversion");
                    return false;
                }

                // Convert window-relative coordinates to screen coordinates
                var screenPoint = screenshot.ToScreenCoordinates(clickPoint);

                // Validate screen coordinates are reasonable
                if (screenPoint.X < 0 || screenPoint.Y < 0 || screenPoint.X > 7680 || screenPoint.Y > 4320)
                {
                    await _loggingService.LogWarningAsync(
                        $"[{StrategyName}] Calculated screen point ({screenPoint.X}, {screenPoint.Y}) appears invalid");
                    return false;
                }

                // Ensure window has focus
                await _automationService.EnsureWindowFocusAsync(windowHandle, cancellationToken);
                await Task.Delay(200, cancellationToken);

                // Move mouse to position for visual feedback
                await _automationService.MoveMouseAsync(screenPoint, cancellationToken);
                await Task.Delay(150, cancellationToken);

                // Perform the click
                await _automationService.ClickAsync(screenPoint, cancellationToken);

                // Post-click delay for UI processing
                if (action.PostNavigationDelayMs > 0)
                {
                    await Task.Delay(action.PostNavigationDelayMs, cancellationToken);
                }

                await _loggingService.LogInfoAsync($"[{StrategyName}] Click executed successfully");
                return true;
            }
            catch (OperationCanceledException)
            {
                await _loggingService.LogDebugAsync($"[{StrategyName}] Click navigation cancelled");
                throw;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"[{StrategyName}] Click navigation failed", ex);
                return false;
            }
        }

        /// <summary>
        /// Calculates the absolute window-relative click point from a relative offset.
        /// The offset is interpreted as a percentage of the template match dimensions.
        /// </summary>
        /// <param name="templateMatch">The template match result providing location and dimensions</param>
        /// <param name="relativeOffset">Relative offset (0.0 to 1.0) within the template region</param>
        /// <returns>Absolute point in window coordinates</returns>
        /// <remarks>
        /// Example: For a template at (100, 100) with size (200, 100) and offset (0.5, 0.5),
        /// the calculated point would be (200, 150) - the center of the template region.
        /// </remarks>
        private Point CalculateAbsoluteClickPoint(TemplateMatchResult templateMatch, RelativeClickOffset relativeOffset)
        {
            // Get template match location and dimensions
            var matchX = templateMatch.WindowRelativePosition.X;
            var matchY = templateMatch.WindowRelativePosition.Y;
            var matchWidth = templateMatch.MatchSize.Width;
            var matchHeight = templateMatch.MatchSize.Height;

            // Calculate absolute position within the template region
            var absoluteX = matchX + (int)(matchWidth * relativeOffset.X);
            var absoluteY = matchY + (int)(matchHeight * relativeOffset.Y);

            return new Point(absoluteX, absoluteY);
        }
    }
}
