using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.Services.AutoLogin.ScreenDetection;
using FFXIManager.Infrastructure;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Handles Windower application launch and initial setup tasks.
    /// Responsible for: LaunchWindower, WaitForWindowerStart, VerifyWindowerLoaded, ClickLaunchButton
    /// </summary>
    public class WindowerLaunchHandler : BaseLoginTaskHandler
    {
        private readonly IProcessUtilityService _processUtilityService;
        private readonly IExternalApplicationService _externalApplicationService;
        private readonly IUIAutomationService _automationService;

        public WindowerLaunchHandler(
            ILoggingService loggingService,
            IScreenshotCaptureService screenshotService,
            ITemplateMatchingService templateService,
            ITemplateManagementService templateManagementService,
            IProcessUtilityService processUtilityService,
            IExternalApplicationService externalApplicationService,
            IUIAutomationService automationService)
            : base(loggingService, screenshotService, templateService, templateManagementService)
        {
            _processUtilityService = processUtilityService ?? throw new ArgumentNullException(nameof(processUtilityService));
            _externalApplicationService = externalApplicationService ?? throw new ArgumentNullException(nameof(externalApplicationService));
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
        }

        public override LoginTaskStep TaskStep => LoginTaskStep.LaunchWindower;

        public override bool CanHandle(AutoLoginSubtask subtask)
        {
            return subtask.TaskStep switch
            {
                LoginTaskStep.LaunchWindower => true,
                LoginTaskStep.WaitForWindowerStart => true,
                LoginTaskStep.VerifyWindowerLoaded => true,
                LoginTaskStep.ClickLaunchButton => true,
                _ => false
            };
        }

        protected override async Task ExecuteHandlerLogicAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IAutoLoginContext context, CancellationToken cancellationToken)
        {
            await _loggingService.LogInfoAsync($"[FLOW] WindowerLaunchHandler.ExecuteHandlerLogicAsync - Processing task step: {subtask.TaskStep} for {queueItem.DisplayName}");

            // Use the injected context directly - no need for internal context storage
            await _loggingService.LogInfoAsync($"[FLOW] Using centralized context for queue item {queueItem.Id} (Data has {context.Data.Count} items)");

            switch (subtask.TaskStep)
            {
                case LoginTaskStep.LaunchWindower:
                    await ExecuteLaunchWindowerAsync(subtask, queueItem, context, cancellationToken);
                    break;

                case LoginTaskStep.WaitForWindowerStart:
                    await ExecuteWaitForWindowerStartAsync(subtask, queueItem, context, cancellationToken);
                    break;

                case LoginTaskStep.VerifyWindowerLoaded:
                    await ExecuteVerifyWindowerLoadedAsync(subtask, queueItem, context, cancellationToken);
                    break;

                case LoginTaskStep.ClickLaunchButton:
                    await ExecuteClickLaunchButtonAsync(subtask, queueItem, context, cancellationToken);
                    break;

                default:
                    throw new NotSupportedException($"Task step {subtask.TaskStep} is not supported by WindowerLaunchHandler");
            }

            await _loggingService.LogDebugAsync($"Completed Windower task: {subtask.TaskStep} for {queueItem.DisplayName}");
        }

        private async Task ExecuteLaunchWindowerAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IAutoLoginContext context, CancellationToken cancellationToken)
        {

            await _loggingService.LogInfoAsync($"[FLOW] Starting ExecuteLaunchWindowerAsync for {queueItem.DisplayName}");
            subtask.Start();

            try
            {
                if (string.IsNullOrEmpty(queueItem.Account?.AccountName))
                {
                    subtask.Fail("Account name is required for Windower launch");
                    return;
                }

                subtask.UpdateProgress(10, "Locating Windower application...");

                // Get all external applications and find Windower
                var applications = await _externalApplicationService.GetApplicationsAsync();
                var windowerApp = applications.FirstOrDefault(app =>
                    app.Name.Contains("Windower", StringComparison.OrdinalIgnoreCase) ||
                    app.ExecutablePath.Contains("windower", StringComparison.OrdinalIgnoreCase));

                if (windowerApp == null)
                {
                    subtask.Fail("Windower application not configured. Please add Windower to External Applications.");
                    return;
                }

                subtask.UpdateProgress(30, "Validating Windower configuration...");

                // Check if Windower is already running
                var existingProcesses = await _processUtilityService.GetProcessesByNamesAsync(new[] { "windower" });
                if (existingProcesses.Any())
                {
                    var processId = existingProcesses.First().ProcessId;
                    subtask.UpdateProgress(100, $"Windower already running (PID: {processId})");
                    context.SetData("ProcessId", processId);
                    await GetWindowHandleForProcess(context, processId);
                    subtask.Complete();
                    return;
                }

                subtask.UpdateProgress(50, $"Starting Windower for {queueItem.Account.AccountName}...");

                // Launch Windower
                bool launchSuccess = await _externalApplicationService.LaunchApplicationAsync(windowerApp);
                if (!launchSuccess)
                {
                    subtask.Fail("Failed to launch Windower application");
                    return;
                }

                subtask.UpdateProgress(70, "Waiting for process startup...");

                // Wait for process to start and get the PID
                var timeout = DateTime.Now.AddSeconds(10);
                int newProcessId = 0;

                while (DateTime.Now < timeout && !cancellationToken.IsCancellationRequested)
                {
                    var processes = await _processUtilityService.GetProcessesByNamesAsync(new[] { "windower" });
                    if (processes.Any())
                    {
                        newProcessId = processes.First().ProcessId;
                        break;
                    }
                    await Task.Delay(500, cancellationToken);
                }

                if (newProcessId == 0)
                {
                    subtask.Fail("Windower process did not start within timeout period");
                    return;
                }

                context.SetData("ProcessId", newProcessId);
                subtask.UpdateProgress(90, $"Windower started (PID: {newProcessId})");

                // Get window handle for the process
                await GetWindowHandleForProcess(context, newProcessId);

                subtask.UpdateProgress(100, "Windower launch completed successfully");
                await _loggingService.LogInfoAsync($"[FLOW] ExecuteLaunchWindowerAsync completed successfully");
                subtask.Complete();
            }
            catch (OperationCanceledException)
            {
                subtask.Cancel();
                throw;
            }
            catch (Exception ex)
            {
                subtask.Fail($"Failed to launch Windower: {ex.Message}");
                await _loggingService.LogErrorAsync($"WindowerLaunchHandler.ExecuteLaunchWindowerAsync failed for {queueItem.Account?.AccountName}", ex);
                throw;
            }
        }

        private async Task GetWindowHandleForProcess(IAutoLoginContext context, int processId)
        {
            // Try to get the main window handle
            var windows = await _processUtilityService.GetProcessWindowsAsync(processId);
            var mainWindow = windows.FirstOrDefault(w => !string.IsNullOrEmpty(w.Title));

            if (mainWindow != null)
            {
                context.SetData("WindowHandle", mainWindow.Handle);
                await _loggingService.LogDebugAsync($"Found Windower window: {mainWindow.Title}");
            }
        }

        private async Task ExecuteWaitForWindowerStartAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IAutoLoginContext context, CancellationToken cancellationToken)
        {

            await _loggingService.LogInfoAsync($"[FLOW] Starting ExecuteWaitForWindowerStartAsync");
            subtask.Start();
            await _loggingService.LogInfoAsync($"[FLOW] ExecuteWaitForWindowerStartAsync - subtask.Start() completed");

            try
            {
                var processId = context.GetValueData<int>("ProcessId");
                if (processId == 0)
                {
                    subtask.Fail("No Windower process ID available from previous step");
                    return;
                }

                subtask.UpdateProgress(20, "Monitoring Windower process startup...");

                // Wait for process to become responsive
                var timeout = DateTime.Now.AddSeconds(15);
                bool isResponsive = false;

                while (DateTime.Now < timeout && !cancellationToken.IsCancellationRequested)
                {
                    await _loggingService.LogInfoAsync($"[FLOW] WaitForWindowerStart - Checking process {processId} responsiveness...");

                    // Check if process is still running
                    if (!_processUtilityService.IsProcessRunning(processId))
                    {
                        subtask.Fail("Windower process terminated unexpectedly");
                        return;
                    }

                    // Get process info to check responsiveness
                    await _loggingService.LogInfoAsync($"[FLOW] WaitForWindowerStart - Getting process info for PID {processId}...");

                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token).Token;
                        var processInfo = await _processUtilityService.GetProcessInfoAsync(processId).WaitAsync(combinedToken);
                        await _loggingService.LogInfoAsync($"[FLOW] WaitForWindowerStart - Process info received, IsResponding: {processInfo?.IsResponding}");

                        if (processInfo?.IsResponding == true)
                        {
                            isResponsive = true;
                            break;
                        }
                    }
                    catch (TimeoutException)
                    {
                        await _loggingService.LogInfoAsync($"[FLOW] WaitForWindowerStart - GetProcessInfoAsync timed out after 5 seconds");
                    }
                    catch (OperationCanceledException)
                    {
                        await _loggingService.LogInfoAsync($"[FLOW] WaitForWindowerStart - GetProcessInfoAsync was cancelled");
                        break;
                    }

                    subtask.UpdateProgress(20 + (int)((DateTime.Now - timeout.AddSeconds(-15)).TotalSeconds * 4),
                        "Waiting for process to become responsive...");
                    await Task.Delay(500, cancellationToken);
                }

                if (!isResponsive)
                {
                    subtask.Fail("Windower process did not become responsive within timeout");
                    return;
                }

                subtask.UpdateProgress(60, "Process responsive, waiting for main window...");

                // Wait for main window to appear
                timeout = DateTime.Now.AddSeconds(10);
                while (DateTime.Now < timeout && !cancellationToken.IsCancellationRequested)
                {
                    await _loggingService.LogInfoAsync($"[FLOW] WaitForWindowerStart - Looking for main window for PID {processId}...");
                    var windows = await _processUtilityService.GetProcessWindowsAsync(processId);
                    await _loggingService.LogInfoAsync($"[FLOW] WaitForWindowerStart - Found {windows?.Count() ?? 0} windows");
                    var mainWindow = windows.FirstOrDefault(w => !string.IsNullOrEmpty(w.Title) && w.IsVisible);

                    if (mainWindow != null)
                    {
                        context.SetData("WindowHandle", mainWindow.Handle);
                        subtask.UpdateProgress(80, $"Main window detected: {mainWindow.Title}");
                        break;
                    }

                    await Task.Delay(500, cancellationToken);
                }

                if (!context.HasData("WindowHandle"))
                {
                    subtask.Fail("Windower main window not found within timeout");
                    return;
                }

                subtask.UpdateProgress(100, "Windower startup completed and window available");
                await _loggingService.LogInfoAsync($"[FLOW] ExecuteWaitForWindowerStartAsync completed successfully");
                subtask.Complete();
            }
            catch (OperationCanceledException)
            {
                subtask.Cancel();
                throw;
            }
            catch (Exception ex)
            {
                subtask.Fail($"Error waiting for Windower startup: {ex.Message}");
                await _loggingService.LogErrorAsync($"WindowerLaunchHandler.ExecuteWaitForWindowerStartAsync failed", ex);
                throw;
            }
        }

        private async Task ExecuteVerifyWindowerLoadedAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IAutoLoginContext context, CancellationToken cancellationToken)
        {
            await _loggingService.LogInfoAsync($"[FLOW] Starting ExecuteVerifyWindowerLoadedAsync");
            subtask.Start();

            try
            {
                var windowHandle = context.GetValueData<IntPtr>("WindowHandle");
                if (windowHandle == IntPtr.Zero)
                {
                    subtask.Fail("No Windower window handle available from previous step");
                    return;
                }

                subtask.UpdateProgress(15, "Waiting for Windower UI initialization...");

                // Wait for UI to stabilize before detection
                await Task.Delay(2000, cancellationToken);

                // Use standardized detection with standard confidence threshold
                var launchArrowMatch = await WaitForScreenDetectionAsync(
                    subtask,
                    "Windower/launch_arrow",
                    windowHandle,
                    "Windower launch arrow",
                    cancellationToken,
                    ScreenDetectionOptions.Default);

                if (launchArrowMatch.Confidence < 0.80f)
                {
                    throw new InvalidOperationException($"Could not detect Windower launch arrow (confidence: {launchArrowMatch.Confidence:P})");
                }

                await _loggingService.LogInfoAsync($"[FLOW] ExecuteVerifyWindowerLoadedAsync completed successfully - UI ready with confidence {launchArrowMatch.Confidence:P}");
                subtask.Complete();
            }
            catch (OperationCanceledException)
            {
                subtask.Cancel();
                throw;
            }
            catch (Exception ex)
            {
                subtask.Fail($"Error verifying Windower UI: {ex.Message}");
                await _loggingService.LogErrorAsync($"WindowerLaunchHandler.ExecuteVerifyWindowerLoadedAsync failed", ex);
                throw;
            }
        }

        private async Task ExecuteClickLaunchButtonAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IAutoLoginContext context, CancellationToken cancellationToken)
        {
            await _loggingService.LogInfoAsync($"[FLOW] Starting ExecuteClickLaunchButtonAsync");
            subtask.Start();

            try
            {
                var windowHandle = context.GetValueData<IntPtr>("WindowHandle");
                if (windowHandle == IntPtr.Zero)
                {
                    subtask.Fail("No Windower window handle available from previous step");
                    return;
                }

                subtask.UpdateProgress(15, "Focusing Windower window...");

                // Ensure window focus
                await _automationService.EnsureWindowFocusAsync(windowHandle, cancellationToken);
                await Task.Delay(500, cancellationToken);

                subtask.UpdateProgress(30, "Locating launch button...");

                // Find the launch arrow using standardized detection
                var launchArrowMatch = await WaitForScreenDetectionAsync(
                    subtask,
                    "Windower/launch_arrow",
                    windowHandle,
                    "launch arrow button",
                    cancellationToken,
                    ScreenDetectionOptions.Quick); // 10-second timeout for button detection

                if (launchArrowMatch.Confidence < 0.80f)
                {
                    throw new InvalidOperationException($"Could not detect launch arrow button (confidence: {launchArrowMatch.Confidence:P})");
                }

                // Use standardized coordinate clicking
                var clickPoint = launchArrowMatch.GetClickPoint();
                await ClickAtCoordinatesAsync(
                    subtask,
                    clickPoint,
                    windowHandle,
                    "launch button",
                    cancellationToken,
                    _automationService);

                subtask.UpdateProgress(75, "Waiting for PlayOnline to start...");

                // Wait for PlayOnline process to start
                await Task.Delay(2000, cancellationToken);
                await WaitForPlayOnlineProcessAsync(subtask, cancellationToken);

                subtask.Complete();
            }
            catch (OperationCanceledException)
            {
                subtask.Cancel();
                throw;
            }
            catch (Exception ex)
            {
                subtask.Fail($"Error clicking launch button: {ex.Message}");
                await _loggingService.LogErrorAsync($"WindowerLaunchHandler.ExecuteClickLaunchButtonAsync failed", ex);
                throw;
            }
        }

        private async Task WaitForPlayOnlineProcessAsync(AutoLoginSubtask subtask, CancellationToken cancellationToken)
        {
            var timeout = DateTime.Now.AddSeconds(10);

            while (DateTime.Now < timeout && !cancellationToken.IsCancellationRequested)
            {
                var polProcesses = await _processUtilityService.GetProcessesByNamesAsync(new[] { "pol" });
                if (polProcesses.Any())
                {
                    var polProcess = polProcesses.First();
                    subtask.UpdateProgress(90, $"PlayOnline started (PID: {polProcess.ProcessId})");
                    await _loggingService.LogDebugAsync($"PlayOnline process detected: PID {polProcess.ProcessId}");
                    return;
                }

                await Task.Delay(500, cancellationToken);
            }

            // Even if we don't detect POL immediately, the click was successful
            subtask.UpdateProgress(100, "Launch button clicked - PlayOnline may be starting");
        }
    }
}