using System;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.Services.AutoLogin.ScreenDetection;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Implementation of generic window handle management operations.
    /// Provides reusable window management functionality across auto-login handlers.
    /// </summary>
    public class WindowHandleManagementService : IWindowHandleManagementService
    {
        private readonly IUIAutomationService _automationService;
        private readonly IScreenshotCaptureService _screenshotService;
        private readonly ILoggingService _loggingService;

        public WindowHandleManagementService(
            IUIAutomationService automationService,
            IScreenshotCaptureService screenshotService,
            ILoggingService loggingService)
        {
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
            _screenshotService = screenshotService ?? throw new ArgumentNullException(nameof(screenshotService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        public IntPtr ValidateWindowContextAsync(
            IAutoLoginContext context,
            string contextKey,
            IntPtr fallbackHandle = default)
        {
            var contextHandle = context.GetValueData<IntPtr>(contextKey);
            return contextHandle != IntPtr.Zero ? contextHandle : fallbackHandle;
        }

        public async Task PrepareWindowForNavigationAsync(
            IntPtr windowHandle,
            TimeSpan delayType,
            CancellationToken cancellationToken)
        {
            await _automationService.EnsureWindowFocusAsync(windowHandle, cancellationToken);
            await Task.Delay(delayType, cancellationToken);
        }

        public async Task<bool> IsWindowHandleValidAsync(
            IntPtr windowHandle,
            CancellationToken cancellationToken)
        {
            if (windowHandle == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                var screenshot = await _screenshotService.CaptureWindowAsync(windowHandle, cancellationToken);
                return screenshot != null && screenshot.IsValid && screenshot.Width > 0 && screenshot.Height > 0;
            }
            catch (Exception ex)
            {
                await _loggingService.LogDebugAsync($"Window handle validation failed: {ex.Message}");
                return false;
            }
        }
    }
}
