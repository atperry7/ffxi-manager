using System;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.Services.AutoLogin.ScreenDetection;

namespace FFXIManager.Services.AutoLogin.Navigation
{
    /// <summary>
    /// Implements keyboard-based navigation strategy for UI interaction.
    /// This strategy is inherently resolution and DPI independent, making it the
    /// preferred approach for auto-login navigation.
    /// </summary>
    /// <remarks>
    /// Keyboard navigation sequences are defined in template metadata JSON files.
    /// Supports Tab, Enter, Arrow keys with configurable repetition and delays.
    /// </remarks>
    public class KeyboardNavigationStrategy : INavigationStrategy
    {
        private readonly IUIAutomationService _automationService;
        private readonly ILoggingService _loggingService;

        public string StrategyName => "Keyboard Navigation";

        public KeyboardNavigationStrategy(
            IUIAutomationService automationService,
            ILoggingService loggingService)
        {
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        public async Task<bool> ExecuteAsync(
            IntPtr windowHandle,
            NavigationAction action,
            TemplateMatchResult? templateMatch,
            CancellationToken cancellationToken)
        {
            if (action.Sequence == null || action.Sequence.Count == 0)
            {
                await _loggingService.LogWarningAsync($"[{StrategyName}] No keyboard sequence defined in navigation action");
                return false;
            }

            try
            {
                await _loggingService.LogInfoAsync($"[{StrategyName}] Executing keyboard sequence with {action.Sequence.Count} actions");

                // Ensure window has focus before starting keyboard sequence
                await _automationService.EnsureWindowFocusAsync(windowHandle, cancellationToken);
                await Task.Delay(200, cancellationToken); // Brief stabilization delay

                // Execute each action in sequence (keyboard or click)
                for (int i = 0; i < action.Sequence.Count; i++)
                {
                    var keyAction = action.Sequence[i];
                    await ExecuteActionAsync(windowHandle, keyAction, templateMatch, i + 1, action.Sequence.Count, cancellationToken);
                }

                // Post-navigation delay for UI to process the sequence
                if (action.PostNavigationDelayMs > 0)
                {
                    await Task.Delay(action.PostNavigationDelayMs, cancellationToken);
                }

                await _loggingService.LogInfoAsync($"[{StrategyName}] Keyboard sequence completed successfully");
                return true;
            }
            catch (OperationCanceledException)
            {
                await _loggingService.LogDebugAsync($"[{StrategyName}] Keyboard navigation cancelled");
                throw;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"[{StrategyName}] Keyboard navigation failed", ex);
                return false;
            }
        }

        /// <summary>
        /// Executes a single action from the sequence (keyboard or mouse click).
        /// </summary>
        private async Task ExecuteActionAsync(
            IntPtr windowHandle,
            KeyboardAction keyAction,
            TemplateMatchResult? templateMatch,
            int stepNumber,
            int totalSteps,
            CancellationToken cancellationToken)
        {
            var description = string.IsNullOrEmpty(keyAction.Description)
                ? $"{keyAction.Action} x{keyAction.Count}"
                : keyAction.Description;

            await _loggingService.LogDebugAsync($"[{StrategyName}] Step {stepNumber}/{totalSteps}: {description}");

            // Handle Click action
            if (keyAction.Action.Equals("Click", StringComparison.OrdinalIgnoreCase))
            {
                if (templateMatch == null)
                {
                    await _loggingService.LogWarningAsync($"[{StrategyName}] Click action requires template match, but none provided");
                    return;
                }

                // Calculate absolute click position from relative coordinates
                var clickPoint = CalculateAbsoluteClickPoint(templateMatch, keyAction.ClickX, keyAction.ClickY);

                // Execute the click the specified number of times
                for (int i = 0; i < keyAction.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await _automationService.ClickWindowRelativeAsync(windowHandle, clickPoint, cancellationToken);

                    // Brief delay between repetitions
                    if (i < keyAction.Count - 1)
                    {
                        await Task.Delay(50, cancellationToken);
                    }
                }
            }
            else
            {
                // Handle keyboard action
                var consoleKey = ParseConsoleKey(keyAction.Action);
                if (consoleKey == null)
                {
                    await _loggingService.LogWarningAsync($"[{StrategyName}] Unsupported keyboard action: {keyAction.Action}");
                    return;
                }

                // Execute the key action the specified number of times
                for (int i = 0; i < keyAction.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await _automationService.SendKeyAsync(consoleKey.Value, windowHandle, cancellationToken);

                    // Brief delay between repetitions to prevent input flooding
                    if (i < keyAction.Count - 1)
                    {
                        await Task.Delay(50, cancellationToken);
                    }
                }
            }

            // Apply configured delay after this action completes
            if (keyAction.DelayMs > 0)
            {
                await Task.Delay(keyAction.DelayMs, cancellationToken);
            }
        }

        /// <summary>
        /// Calculates the absolute window-relative click point from relative coordinates.
        /// The coordinates are interpreted as percentages of the template match dimensions.
        /// </summary>
        /// <param name="templateMatch">The template match result providing location and dimensions</param>
        /// <param name="relativeX">Relative X coordinate (0.0 = left edge, 0.5 = center, 1.0 = right edge)</param>
        /// <param name="relativeY">Relative Y coordinate (0.0 = top edge, 0.5 = center, 1.0 = bottom edge)</param>
        /// <returns>Absolute point in window coordinates</returns>
        private System.Drawing.Point CalculateAbsoluteClickPoint(TemplateMatchResult templateMatch, double relativeX, double relativeY)
        {
            // Get template match location and dimensions
            var matchX = templateMatch.WindowRelativePosition.X;
            var matchY = templateMatch.WindowRelativePosition.Y;
            var matchWidth = templateMatch.MatchSize.Width;
            var matchHeight = templateMatch.MatchSize.Height;

            // Calculate absolute position within the template region
            var absoluteX = matchX + (int)(matchWidth * relativeX);
            var absoluteY = matchY + (int)(matchHeight * relativeY);

            return new System.Drawing.Point(absoluteX, absoluteY);
        }

        /// <summary>
        /// Parses a string action name into a ConsoleKey value.
        /// Supports common navigation keys used in auto-login scenarios.
        /// </summary>
        private ConsoleKey? ParseConsoleKey(string action)
        {
            return action?.ToUpperInvariant() switch
            {
                "ENTER" or "RETURN" => ConsoleKey.Enter,
                "TAB" => ConsoleKey.Tab,
                "DOWNARROW" or "DOWN" => ConsoleKey.DownArrow,
                "UPARROW" or "UP" => ConsoleKey.UpArrow,
                "LEFTARROW" or "LEFT" => ConsoleKey.LeftArrow,
                "RIGHTARROW" or "RIGHT" => ConsoleKey.RightArrow,
                "ESCAPE" or "ESC" => ConsoleKey.Escape,
                "SPACEBAR" or "SPACE" => ConsoleKey.Spacebar,
                "HOME" => ConsoleKey.Home,
                "END" => ConsoleKey.End,
                "PAGEUP" => ConsoleKey.PageUp,
                "PAGEDOWN" => ConsoleKey.PageDown,
                _ => null
            };
        }
    }
}
