using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.Services.AutoLogin.ScreenDetection;

namespace FFXIManager.Services.AutoLogin.Navigation
{
    /// <summary>
    /// Implements a hybrid navigation strategy that executes sequences containing both
    /// keyboard and click actions. Each step in the sequence is executed in order,
    /// providing flexible, resolution-independent navigation.
    /// </summary>
    /// <remarks>
    /// The hybrid approach:
    /// 1. Executes mixed sequences of keyboard actions (Tab, Enter, etc.) and click actions
    /// 2. Each action type is delegated to the appropriate specialized strategy
    /// 3. Provides comprehensive logging for troubleshooting
    /// 4. Supports resolution/DPI independence through relative click coordinates
    /// </remarks>
    public class HybridNavigationStrategy : INavigationStrategy
    {
        private readonly KeyboardNavigationStrategy _keyboardStrategy;
        private readonly RelativeClickNavigationStrategy _clickStrategy;
        private readonly ILoggingService _loggingService;

        public string StrategyName => "Hybrid Navigation (Sequence-Based)";

        public HybridNavigationStrategy(
            IUIAutomationService automationService,
            IScreenshotCaptureService screenshotService,
            ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));

            // Initialize component strategies
            _keyboardStrategy = new KeyboardNavigationStrategy(automationService, loggingService);
            _clickStrategy = new RelativeClickNavigationStrategy(automationService, screenshotService, loggingService);
        }

        public async Task<bool> ExecuteAsync(
            IntPtr windowHandle,
            NavigationAction action,
            TemplateMatchResult? templateMatch,
            CancellationToken cancellationToken)
        {
            await _loggingService.LogInfoAsync($"[{StrategyName}] Starting hybrid navigation");

            // Validate that we have a sequence to execute
            if (action.Sequence == null || action.Sequence.Count == 0)
            {
                await _loggingService.LogErrorAsync($"[{StrategyName}] No navigation sequence defined");
                return false;
            }

            try
            {
                // Execute each step in the sequence
                for (int i = 0; i < action.Sequence.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var step = action.Sequence[i];

                    if (IsClickStep(step))
                    {
                        // Execute click action - create a NavigationAction with the click step in the sequence
                        var clickAction = new NavigationAction
                        {
                            PostNavigationDelayMs = step.DelayMs
                        };
                        clickAction.Sequence.Add(step);

                        await _loggingService.LogDebugAsync($"[{StrategyName}] Step {i + 1}/{action.Sequence.Count}: Click at ({step.ClickX:P0}, {step.ClickY:P0}) - {step.Description ?? "no description"}");

                        bool clickSuccess = await _clickStrategy.ExecuteAsync(
                            windowHandle,
                            clickAction,
                            templateMatch,
                            cancellationToken);

                        if (!clickSuccess)
                        {
                            await _loggingService.LogErrorAsync($"[{StrategyName}] Click step {i + 1} failed");
                            return false;
                        }
                    }
                    else
                    {
                        // Execute keyboard action
                        var keyAction = new NavigationAction
                        {
                            PostNavigationDelayMs = 0
                        };
                        keyAction.Sequence.Add(step);

                        await _loggingService.LogDebugAsync($"[{StrategyName}] Step {i + 1}/{action.Sequence.Count}: Keyboard '{step.Action}' x{step.Count} - {step.Description ?? "no description"}");

                        bool keySuccess = await _keyboardStrategy.ExecuteAsync(
                            windowHandle,
                            keyAction,
                            templateMatch,
                            cancellationToken);

                        if (!keySuccess)
                        {
                            await _loggingService.LogErrorAsync($"[{StrategyName}] Keyboard step {i + 1} failed");
                            return false;
                        }
                    }
                }

                // Apply post-navigation delay if specified
                if (action.PostNavigationDelayMs > 0)
                {
                    await _loggingService.LogDebugAsync($"[{StrategyName}] Applying post-navigation delay: {action.PostNavigationDelayMs}ms");
                    await Task.Delay(action.PostNavigationDelayMs, cancellationToken);
                }

                await _loggingService.LogInfoAsync($"[{StrategyName}] ✓ Navigation sequence completed successfully ({action.Sequence.Count} steps)");
                return true;
            }
            catch (OperationCanceledException)
            {
                await _loggingService.LogWarningAsync($"[{StrategyName}] Navigation cancelled");
                throw;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"[{StrategyName}] Navigation sequence failed", ex);
                return false;
            }
        }

        /// <summary>
        /// Determines if a keyboard action is actually a click action
        /// </summary>
        private static bool IsClickStep(KeyboardAction step)
            => step.Action?.Trim().Equals("Click", StringComparison.OrdinalIgnoreCase) == true;
    }
}
