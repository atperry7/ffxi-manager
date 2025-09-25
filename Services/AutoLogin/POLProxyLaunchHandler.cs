using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.Services.AutoLogin.ScreenDetection;
using FFXIManager.Services.AutoLogin.Configuration;
using FFXIManager.Infrastructure;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Handles POL Proxy application launch and monitoring within the auto-login system.
    /// This handler manages the complete lifecycle of detecting, launching, and verifying POL Proxy startup
    /// as an optional preliminary step before Windower launch.
    /// 
    /// Supported operations:
    /// - LaunchPOLProxy: Discovers and launches the POL Proxy application if configured
    /// - CheckPOLProxyStatus: Validates POL Proxy configuration and current running status
    /// - WaitForPOLProxyStart: Monitors process startup and responsiveness
    /// 
    /// The handler leverages centralized configuration for timeouts, process names, and progress milestones
    /// to ensure maintainable and consistent behavior. POL Proxy launch is designed to be non-blocking -
    /// if POL Proxy is not configured or fails to launch, the auto-login process continues normally.
    /// 
    /// Dependencies:
    /// - IProcessUtilityService: For process management and monitoring
    /// - IExternalApplicationService: For application discovery and launching
    /// - ILoggingService: For comprehensive logging and troubleshooting
    /// 
    /// Configuration:
    /// All timeout values, process names, and progress milestones are centralized in POLProxyLaunchConfiguration
    /// to support easy maintenance and system-specific adjustments.
    /// </summary>
    public class POLProxyLaunchHandler : BaseLoginTaskHandler
    {
        private readonly IProcessUtilityService _processUtilityService;
        private readonly IExternalApplicationService _externalApplicationService;

        public POLProxyLaunchHandler(
            ILoggingService loggingService,
            IScreenshotCaptureService screenshotService,
            ITemplateMatchingService templateService,
            ITemplateManagementService templateManagementService,
            IProcessUtilityService processUtilityService,
            IExternalApplicationService externalApplicationService)
            : base(loggingService, screenshotService, templateService, templateManagementService)
        {
            _processUtilityService = processUtilityService ?? throw new ArgumentNullException(nameof(processUtilityService));
            _externalApplicationService = externalApplicationService ?? throw new ArgumentNullException(nameof(externalApplicationService));
        }

        public override LoginTaskStep TaskStep => LoginTaskStep.LaunchPOLProxy;

        /// <summary>
        /// Determines if this handler can process the specified auto-login subtask.
        /// Supports all POL Proxy-related operations from configuration check through launch verification.
        /// </summary>
        /// <param name="subtask">The subtask to evaluate for handler compatibility</param>
        /// <returns>True if this handler can process the subtask, false otherwise</returns>
        /// <remarks>
        /// Supported task steps:
        /// - LaunchPOLProxy: Configuration detection, process discovery, and application launch
        /// - CheckPOLProxyStatus: Configuration validation and running status verification
        /// - WaitForPOLProxyStart: Process startup monitoring and responsiveness verification
        /// </remarks>
        public override bool CanHandle(AutoLoginSubtask subtask)
        {
            return subtask.TaskStep switch
            {
                LoginTaskStep.LaunchPOLProxy => true,
                LoginTaskStep.CheckPOLProxyStatus => true,
                LoginTaskStep.WaitForPOLProxyStart => true,
                _ => false
            };
        }

        protected override async Task ExecuteHandlerLogicAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IAutoLoginContext context, CancellationToken cancellationToken)
        {
            await _loggingService.LogInfoAsync($"[FLOW] POLProxyLaunchHandler.ExecuteHandlerLogicAsync - Processing task step: {subtask.TaskStep} for {queueItem.DisplayName}");

            // Use the injected context directly - no need for internal context storage
            await _loggingService.LogInfoAsync($"[FLOW] Using centralized context for queue item {queueItem.Id} (Data has {context.Data.Count} items)");

            switch (subtask.TaskStep)
            {
                case LoginTaskStep.LaunchPOLProxy:
                    await ExecuteLaunchPOLProxyAsync(subtask, queueItem, context, cancellationToken);
                    break;

                case LoginTaskStep.CheckPOLProxyStatus:
                    await ExecuteCheckPOLProxyStatusAsync(subtask, queueItem, context, cancellationToken);
                    break;

                case LoginTaskStep.WaitForPOLProxyStart:
                    await ExecuteWaitForPOLProxyStartAsync(subtask, queueItem, context, cancellationToken);
                    break;

                default:
                    throw new NotSupportedException($"Task step {subtask.TaskStep} is not supported by POLProxyLaunchHandler");
            }

            await _loggingService.LogDebugAsync($"Completed POL Proxy task: {subtask.TaskStep} for {queueItem.DisplayName}");
        }

        /// <summary>
        /// Executes the POL Proxy application launch process with comprehensive error handling and progress reporting.
        /// This method handles the complete workflow from application discovery to process startup verification.
        /// 
        /// Process flow:
        /// 1. Detects if POL Proxy is configured using existing patterns
        /// 2. Checks for existing running instances (with graceful skipping)
        /// 3. Launches new instance if needed and configured
        /// 4. Monitors process startup with configurable timeout
        /// 5. Reports success, skip, or failure status
        /// 
        /// Error handling:
        /// - POL Proxy not configured: Gracefully skips with informational message
        /// - Application not found: Skips with configuration guidance
        /// - Launch failures: Reports failure but allows continuation (non-blocking)
        /// - Startup timeout: Reports timeout with process detection details
        /// 
        /// The design philosophy is non-blocking: POL Proxy launch failure should not prevent
        /// the auto-login process from continuing with Windower launch.
        /// </summary>
        /// <param name="subtask">The subtask for progress reporting and status management</param>
        /// <param name="queueItem">The queue item containing account and configuration details</param>
        /// <param name="context">The shared context for storing process and configuration information</param>
        /// <param name="cancellationToken">Token for operation cancellation</param>
        private async Task ExecuteLaunchPOLProxyAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IAutoLoginContext context, CancellationToken cancellationToken)
        {
            await _loggingService.LogInfoAsync($"[FLOW] Starting ExecuteLaunchPOLProxyAsync for {queueItem.DisplayName}");
            subtask.Start();

            try
            {
                // Step 1: Configuration Detection
                // Check if POL Proxy is configured using the same patterns as PlayOnlineAuthHandler
                // If user has configured it in External Applications, they want us to manage it
                subtask.UpdateProgress(POLProxyLaunchConfiguration.ProgressMilestones.ConfigurationDetection, "Detecting POL Proxy configuration...");
                
                var polProxyApp = await _externalApplicationService.FindApplicationByPatternAsync(
                    PlayOnlineAuthConfiguration.ApplicationPatterns.POLProxyNamePatterns,
                    PlayOnlineAuthConfiguration.ApplicationPatterns.POLProxyPathPatterns);

                if (polProxyApp == null)
                {
                    // POL Proxy not configured - this is normal and expected for many users
                    await _loggingService.LogInfoAsync("POL Proxy not configured - skipping auto-launch (this is normal if you don't use POL Proxy)");
                    subtask.Skip("POL Proxy not configured");
                    context.SetData("POLProxyConfigured", false);
                    return;
                }

                await _loggingService.LogInfoAsync($"POL Proxy detected: {polProxyApp.Name} at {polProxyApp.ExecutablePath}");
                context.SetData("POLProxyConfigured", true);
                context.SetData("POLProxyApplication", polProxyApp);

                // Step 2: Process Status Check  
                subtask.UpdateProgress(POLProxyLaunchConfiguration.ProgressMilestones.ProcessStatusCheck, "Checking POL Proxy running status...");
                
                var existingProcesses = await _processUtilityService.GetProcessesByNamesAsync(POLProxyLaunchConfiguration.ProcessNames.POLProxyVariations);
                if (existingProcesses.Any())
                {
                    // POL Proxy already running - skip launch
                    var processId = existingProcesses.First().ProcessId;
                    await _loggingService.LogInfoAsync($"POL Proxy already running (PID: {processId}) - skipping launch");
                    subtask.Skip($"POL Proxy already running (PID: {processId})");
                    context.SetData("POLProxyProcessId", processId);
                    context.SetData("POLProxyLaunchResult", "AlreadyRunning");
                    return;
                }

                // Step 3: Application Launch
                subtask.UpdateProgress(POLProxyLaunchConfiguration.ProgressMilestones.ApplicationLaunch, $"Launching POL Proxy: {polProxyApp.Name}...");
                
                bool launchSuccess = await _externalApplicationService.LaunchApplicationAsync(polProxyApp);
                if (!launchSuccess)
                {
                    // Launch failed - log error but continue (non-blocking)
                    var errorMessage = "Failed to launch POL Proxy application";
                    await _loggingService.LogWarningAsync(errorMessage);
                    subtask.Skip($"{errorMessage} - continuing without POL Proxy");
                    context.SetData("POLProxyLaunchResult", "LaunchFailed");
                    return;
                }

                await _loggingService.LogInfoAsync("POL Proxy launch initiated successfully");

                // Step 4: Process Startup Verification
                subtask.UpdateProgress(POLProxyLaunchConfiguration.ProgressMilestones.ProcessStartupVerification, "Verifying POL Proxy startup...");
                
                int newProcessId = await GetLaunchedPOLProxyProcessId(polProxyApp, subtask, cancellationToken);
                
                if (newProcessId == 0)
                {
                    // Startup verification failed - log warning but continue
                    var errorMessage = "POL Proxy process did not start within timeout period";
                    await _loggingService.LogWarningAsync(errorMessage);
                    subtask.Skip($"{errorMessage} - continuing without verification");
                    context.SetData("POLProxyLaunchResult", "StartupTimeout");
                    return;
                }

                // Step 5: Success - Store results
                context.SetData("POLProxyProcessId", newProcessId);
                context.SetData("POLProxyLaunchResult", "Success");
                
                subtask.UpdateProgress(POLProxyLaunchConfiguration.ProgressMilestones.PostLaunchStabilization, "POL Proxy startup verified, allowing stabilization...");
                
                // Allow POL Proxy to stabilize
                await Task.Delay(POLProxyLaunchConfiguration.Timeouts.PostLaunchDelay, cancellationToken);

                subtask.UpdateProgress(POLProxyLaunchConfiguration.ProgressMilestones.OperationComplete, $"POL Proxy launched successfully (PID: {newProcessId})");
                await _loggingService.LogInfoAsync($"[FLOW] ExecuteLaunchPOLProxyAsync completed successfully - POL Proxy PID: {newProcessId}");
                subtask.Complete();
            }
            catch (OperationCanceledException)
            {
                subtask.Cancel();
                throw;
            }
            catch (Exception ex)
            {
                // Log error but don't fail - POL Proxy launch is non-critical
                var errorMessage = $"Error during POL Proxy launch: {ex.Message}";
                await _loggingService.LogWarningAsync(errorMessage, ex);
                subtask.Skip($"{errorMessage} - continuing auto-login without POL Proxy");
                context.SetData("POLProxyLaunchResult", "Exception");
            }
        }

        /// <summary>
        /// Executes POL Proxy status checking as a separate subtask.
        /// This provides detailed status information and validation for troubleshooting.
        /// </summary>
        /// <param name="subtask">Current subtask for progress reporting</param>
        /// <param name="queueItem">Queue item for context</param>
        /// <param name="context">AutoLogin context for data retrieval</param>
        /// <param name="cancellationToken">Cancellation token</param>
        private async Task ExecuteCheckPOLProxyStatusAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IAutoLoginContext context, CancellationToken cancellationToken)
        {
            await _loggingService.LogInfoAsync($"[FLOW] Starting ExecuteCheckPOLProxyStatusAsync");
            subtask.Start();

            try
            {
                // Check if POL Proxy was configured in the previous step
                var polProxyConfigured = context.GetValueData<bool>("POLProxyConfigured");
                
                if (!polProxyConfigured)
                {
                    subtask.Skip("POL Proxy not configured - no status to check");
                    return;
                }

                // Get the launch result from the previous step
                var launchResult = context.GetData<string>("POLProxyLaunchResult") ?? "Unknown";
                var processId = context.GetValueData<int>("POLProxyProcessId");

                subtask.UpdateProgress(50, $"Checking POL Proxy status - Launch result: {launchResult}");

                switch (launchResult)
                {
                    case "Success":
                        if (processId > 0 && _processUtilityService.IsProcessRunning(processId))
                        {
                            subtask.UpdateProgress(100, $"POL Proxy running successfully (PID: {processId})");
                        }
                        else
                        {
                            subtask.UpdateProgress(100, "POL Proxy launch reported success but process not detected");
                        }
                        break;

                    case "AlreadyRunning":
                        subtask.UpdateProgress(100, $"POL Proxy was already running (PID: {processId})");
                        break;

                    case "LaunchFailed":
                    case "StartupTimeout":
                    case "Exception":
                        subtask.UpdateProgress(100, $"POL Proxy launch encountered issues: {launchResult}");
                        break;

                    default:
                        subtask.UpdateProgress(100, $"POL Proxy status unknown: {launchResult}");
                        break;
                }

                await _loggingService.LogInfoAsync($"[FLOW] ExecuteCheckPOLProxyStatusAsync completed - Result: {launchResult}");
                subtask.Complete();
            }
            catch (OperationCanceledException)
            {
                subtask.Cancel();
                throw;
            }
            catch (Exception ex)
            {
                subtask.Fail($"Error checking POL Proxy status: {ex.Message}");
                await _loggingService.LogErrorAsync($"POLProxyLaunchHandler.ExecuteCheckPOLProxyStatusAsync failed", ex);
                throw;
            }
        }

        /// <summary>
        /// Executes POL Proxy startup waiting as a separate subtask.
        /// Provides detailed monitoring and verification of process startup.
        /// </summary>
        /// <param name="subtask">Current subtask for progress reporting</param>
        /// <param name="queueItem">Queue item for context</param>
        /// <param name="context">AutoLogin context for data retrieval</param>
        /// <param name="cancellationToken">Cancellation token</param>
        private async Task ExecuteWaitForPOLProxyStartAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IAutoLoginContext context, CancellationToken cancellationToken)
        {
            await _loggingService.LogInfoAsync($"[FLOW] Starting ExecuteWaitForPOLProxyStartAsync");
            subtask.Start();

            try
            {
                // Check if POL Proxy was configured and launched
                var polProxyConfigured = context.GetValueData<bool>("POLProxyConfigured");
                var launchResult = context.GetData<string>("POLProxyLaunchResult") ?? "Unknown";
                
                if (!polProxyConfigured)
                {
                    subtask.Skip("POL Proxy not configured - no startup to wait for");
                    return;
                }

                if (launchResult != "Success")
                {
                    subtask.Skip($"POL Proxy launch was not successful ({launchResult}) - skipping startup wait");
                    return;
                }

                var processId = context.GetValueData<int>("POLProxyProcessId");
                if (processId == 0)
                {
                    subtask.Skip("No POL Proxy process ID available from previous step");
                    return;
                }

                subtask.UpdateProgress(25, $"Monitoring POL Proxy startup (PID: {processId})...");

                // Verify process is still running and responsive
                await WaitForPOLProxyResponsiveness(processId, subtask, cancellationToken);

                subtask.UpdateProgress(100, $"POL Proxy startup completed successfully (PID: {processId})");
                await _loggingService.LogInfoAsync($"[FLOW] ExecuteWaitForPOLProxyStartAsync completed successfully");
                subtask.Complete();
            }
            catch (OperationCanceledException)
            {
                subtask.Cancel();
                throw;
            }
            catch (Exception ex)
            {
                // Don't fail hard - POL Proxy startup issues shouldn't block auto-login
                var errorMessage = $"Error waiting for POL Proxy startup: {ex.Message}";
                await _loggingService.LogWarningAsync(errorMessage, ex);
                subtask.Skip($"{errorMessage} - continuing auto-login");
            }
        }

        /// <summary>
        /// Waits for POL Proxy process to become responsive using base class retry mechanisms.
        /// Monitors process health and responsiveness with timeout and progress reporting.
        /// </summary>
        /// <param name="processId">The process ID to monitor</param>
        /// <param name="subtask">The subtask for progress reporting</param>
        /// <param name="cancellationToken">Cancellation token for operation cancellation</param>
        private async Task WaitForPOLProxyResponsiveness(int processId, AutoLoginSubtask subtask, CancellationToken cancellationToken)
        {
            subtask.UpdateProgress(40, "Verifying POL Proxy process responsiveness...");

            // Use base class retry mechanism for consistent error handling
            await ExecuteWithRetryAsync(
                async (ct) =>
                {
                    await _loggingService.LogDebugAsync($"[FLOW] WaitForPOLProxyResponsiveness - Checking process {processId} responsiveness...");

                    // Check if process is still running
                    if (!_processUtilityService.IsProcessRunning(processId))
                    {
                        throw new InvalidOperationException("POL Proxy process terminated unexpectedly");
                    }

                    // Get process info to check responsiveness with timeout
                    var processInfo = await _processUtilityService.GetProcessInfoAsync(processId);
                    if (processInfo?.IsResponding != true)
                    {
                        throw new InvalidOperationException("POL Proxy process not yet responsive");
                    }

                    await _loggingService.LogDebugAsync($"[FLOW] WaitForPOLProxyResponsiveness - Process {processId} is responsive");
                    return true; // Success - process is responsive
                },
                "POL Proxy responsiveness check",
                maxRetries: (int)(POLProxyLaunchConfiguration.Timeouts.ProcessResponsiveness.TotalSeconds / 
                                 POLProxyLaunchConfiguration.PollingIntervals.ResponsivenessCheck.TotalSeconds),
                baseDelayMs: (int)POLProxyLaunchConfiguration.PollingIntervals.ResponsivenessCheck.TotalMilliseconds,
                cancellationToken);

            subtask.UpdateProgress(75, "POL Proxy process is responsive and ready");
        }

        /// <summary>
        /// Gets the process ID of the launched POL Proxy application and verifies it's running.
        /// The ExternalApplicationService.LaunchApplicationAsync already adds the PID when launching,
        /// so we can get it directly and verify it's still accessible and running.
        /// </summary>
        /// <param name="polProxyApp">The POL Proxy application that was just launched</param>
        /// <param name="subtask">The subtask for progress reporting</param>
        /// <param name="cancellationToken">Cancellation token for operation cancellation</param>
        /// <returns>The process ID of the launched POL Proxy instance, or 0 if verification fails</returns>
        /// <remarks>
        /// This approach is more reliable than waiting for monitoring confirmation because:
        /// 1. The LaunchApplicationAsync method already recorded the PID immediately
        /// 2. We just need to verify the process is still running and accessible
        /// 3. Avoids race conditions with the monitoring system's detection timing
        /// 4. Faster response time for successful launches
        /// </remarks>
        private async Task<int> GetLaunchedPOLProxyProcessId(ExternalApplication polProxyApp, AutoLoginSubtask subtask, CancellationToken cancellationToken)
        {
            // First, check if the application already has process IDs from the launch
            if (polProxyApp.ProcessIds.Any())
            {
                var launchedProcessId = polProxyApp.ProcessIds.First();
                await _loggingService.LogInfoAsync($"[FLOW] Found launched POL Proxy process ID: {launchedProcessId}");
                
                // Verify the process is actually running and accessible
                if (_processUtilityService.IsProcessRunning(launchedProcessId))
                {
                    await _loggingService.LogInfoAsync($"[FLOW] POL Proxy process {launchedProcessId} confirmed running");
                    return launchedProcessId;
                }
                else
                {
                    await _loggingService.LogWarningAsync($"[FLOW] POL Proxy process {launchedProcessId} is not running or not accessible");
                }
            }
            
            // Fallback: Wait a short time for the process to appear in the application's process list
            var timeout = DateTime.UtcNow.Add(POLProxyLaunchConfiguration.Timeouts.ProcessStartup);
            
            await _loggingService.LogInfoAsync($"[FLOW] Waiting for POL Proxy process to appear in application tracking...");
            
            while (DateTime.UtcNow < timeout && !cancellationToken.IsCancellationRequested)
            {
                // Refresh application status
                await _externalApplicationService.RefreshApplicationStatusAsync(polProxyApp);
                
                if (polProxyApp.ProcessIds.Any())
                {
                    var detectedProcessId = polProxyApp.ProcessIds.First();
                    
                    // Verify the process is actually running
                    if (_processUtilityService.IsProcessRunning(detectedProcessId))
                    {
                        await _loggingService.LogInfoAsync($"[FLOW] POL Proxy process {detectedProcessId} detected and confirmed running");
                        return detectedProcessId;
                    }
                }
                
                // Update progress
                var elapsed = DateTime.UtcNow - (timeout - POLProxyLaunchConfiguration.Timeouts.ProcessStartup);
                var progressPercent = POLProxyLaunchConfiguration.ProgressMilestones.ProcessStartupVerification + 
                    (int)((elapsed.TotalSeconds / POLProxyLaunchConfiguration.Timeouts.ProcessStartup.TotalSeconds) * 
                    (POLProxyLaunchConfiguration.ProgressMilestones.PostLaunchStabilization - POLProxyLaunchConfiguration.ProgressMilestones.ProcessStartupVerification));
                subtask.UpdateProgress(Math.Min(progressPercent, POLProxyLaunchConfiguration.ProgressMilestones.PostLaunchStabilization - 1),
                    "Verifying launched POL Proxy process...");
                
                await Task.Delay(POLProxyLaunchConfiguration.PollingIntervals.ProcessStartupCheck, cancellationToken);
            }
            
            await _loggingService.LogWarningAsync($"[FLOW] Could not verify POL Proxy process startup within {POLProxyLaunchConfiguration.Timeouts.ProcessStartup.TotalSeconds}s");
            return 0; // Verification failed
        }
    }
}