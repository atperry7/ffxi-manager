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
    /// Implements a hybrid navigation strategy that attempts keyboard navigation first,
    /// then falls back to relative click navigation if keyboard fails.
    /// This provides the best reliability across different configurations and UI states.
    /// </summary>
    /// <remarks>
    /// The hybrid approach:
    /// 1. Attempts keyboard navigation (resolution/DPI independent, preferred)
    /// 2. Falls back to relative clicking if keyboard fails (still resolution/DPI aware)
    /// 3. Provides comprehensive logging for troubleshooting
    /// </remarks>
    public class HybridNavigationStrategy : INavigationStrategy
    {
        private readonly KeyboardNavigationStrategy _keyboardStrategy;
        private readonly RelativeClickNavigationStrategy _clickStrategy;
        private readonly ILoggingService _loggingService;

        public string StrategyName => "Hybrid Navigation (Keyboard → Click)";

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

            // If the sequence mixes keyboard and click steps, execute step-by-step
            if (action.Sequence != null && action.Sequence.Any(s => IsClickStep(s)))
            {
                try
                {
                    for (int i = 0; i < action.Sequence.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var step = action.Sequence[i];

                        if (IsClickStep(step))
                        {
                            var clickAction = new NavigationAction
                            {
                                Type = NavigationType.RelativeClick,
                                ClickOffset = new RelativeClickOffset { X = step.ClickX, Y = step.ClickY, Description = step.Description },
                                PostNavigationDelayMs = step.DelayMs
                            };

                            var ok = await _clickStrategy.ExecuteAsync(windowHandle, clickAction, templateMatch, cancellationToken);
                            if (!ok)
                            {
                                await _loggingService.LogWarningAsync($"[{StrategyName}] Click step failed at index {i}");
                                throw new InvalidOperationException("Click step failed");
                            }
                        }
                        else
                        {
                            var keyAction = new NavigationAction { Type = NavigationType.Keyboard, PostNavigationDelayMs = 0 };
                            keyAction.Sequence.Add(step);
                            var ok = await _keyboardStrategy.ExecuteAsync(windowHandle, keyAction, templateMatch, cancellationToken);
                            if (!ok)
                            {
                                await _loggingService.LogWarningAsync($"[{StrategyName}] Keyboard step failed at index {i}");
                                throw new InvalidOperationException("Keyboard step failed");
                            }
                        }
                    }

                    if (action.PostNavigationDelayMs > 0)
                        await Task.Delay(action.PostNavigationDelayMs, cancellationToken);

                    await _loggingService.LogInfoAsync($"[{StrategyName}] Mixed sequence executed successfully");
                    return true;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    await _loggingService.LogWarningAsync($"[{StrategyName}] Mixed sequence failed, attempting fallback: {ex.Message}");
                }
            }

            // Phase 1: Attempt keyboard navigation
            bool keyboardSuccess = false;
            if (action.Sequence != null && action.Sequence.Count > 0)
            {
                await _loggingService.LogInfoAsync($"[{StrategyName}] Phase 1: Attempting keyboard navigation");

                try
                {
                    keyboardSuccess = await _keyboardStrategy.ExecuteAsync(
                        windowHandle,
                        action,
                        templateMatch,
                        cancellationToken);

                    if (keyboardSuccess)
                    {
                        await _loggingService.LogInfoAsync($"[{StrategyName}] ✓ Keyboard navigation succeeded");
                        return true;
                    }
                    else
                    {
                        await _loggingService.LogWarningAsync($"[{StrategyName}] ✗ Keyboard navigation failed, attempting fallback");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await _loggingService.LogWarningAsync($"[{StrategyName}] Keyboard navigation threw exception, attempting fallback: {ex.Message}");
                }
            }
            else
            {
                await _loggingService.LogDebugAsync($"[{StrategyName}] No keyboard sequence defined, skipping to click fallback");
            }

            // Phase 2: Fallback to click navigation
            if (action.ClickOffset != null || action.Fallback != null)
            {
                await _loggingService.LogInfoAsync($"[{StrategyName}] Phase 2: Attempting click navigation fallback");

                // Use explicit fallback action if defined, otherwise use the primary action's click offset
                var clickAction = action.Fallback ?? action;

                try
                {
                    bool clickSuccess = await _clickStrategy.ExecuteAsync(
                        windowHandle,
                        clickAction,
                        templateMatch,
                        cancellationToken);

                    if (clickSuccess)
                    {
                        await _loggingService.LogInfoAsync($"[{StrategyName}] ✓ Click navigation fallback succeeded");
                        return true;
                    }
                    else
                    {
                        await _loggingService.LogErrorAsync($"[{StrategyName}] ✗ Both keyboard and click navigation failed");
                        return false;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync($"[{StrategyName}] Click navigation fallback failed", ex);
                    return false;
                }
            }

            await _loggingService.LogErrorAsync($"[{StrategyName}] No fallback navigation defined");
            return false;
        }

        private static bool IsClickStep(KeyboardAction step)
            => step.Action?.Trim().Equals("Click", System.StringComparison.OrdinalIgnoreCase) == true;
    }
}
