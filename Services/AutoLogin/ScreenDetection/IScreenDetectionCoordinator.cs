using System;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services.AutoLogin.ScreenDetection
{
    public interface IScreenDetectionCoordinator
    {
        Task<TemplateMatchResult> WaitForScreenDetectionAsync(
            AutoLoginSubtask subtask,
            string templatePath,
            IntPtr windowHandle,
            string screenDescription,
            float confidenceThreshold,
            int tolerance,
            CancellationToken cancellationToken,
            ScreenDetectionOptions? options = null);

        Task<TemplateMatchResult> WaitForScreenDetectionWithHandleRefreshAsync(
            AutoLoginSubtask subtask,
            string templatePath,
            IntPtr initialWindowHandle,
            Func<CancellationToken, Task<IntPtr>> refreshHandleAsync,
            string screenDescription,
            float confidenceThreshold,
            int tolerance,
            CancellationToken cancellationToken,
            ScreenDetectionOptions? options = null);

        Task<WindowScreenshot> CaptureScreenshotWithLogging(
            IntPtr windowHandle,
            string purpose,
            CancellationToken cancellationToken,
            int retryCount = 2);
    }
}

