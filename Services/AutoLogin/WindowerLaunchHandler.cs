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
using FFXIManager.Services.AutoLogin.Configuration;
using FFXIManager.Infrastructure;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Handles Windower application launch and initial setup tasks within the auto-login system.
    /// This handler manages the complete lifecycle of starting Windower and preparing it for PlayOnline launch.
    /// 
    /// Supported operations:
    /// - LaunchWindower: Discovers and launches the Windower application
    /// - WaitForWindowerStart: Monitors process startup and responsiveness
    /// - VerifyWindowerLoaded: Validates UI is ready for interaction
    /// - ClickLaunchButton: Initiates PlayOnline launch through Windower
    /// 
    /// The handler leverages centralized configuration for timeouts, process names, and UI templates
    /// to ensure maintainable and consistent behavior across different system configurations.
    /// 
    /// Dependencies:
    /// - IProcessUtilityService: For process management and window detection
    /// - IExternalApplicationService: For application discovery and launching
    /// - IUIAutomationService: For window focus and UI interaction
    /// 
    /// Configuration:
    /// All timeout values, process names, and UI templates are centralized in WindowerLaunchConfiguration
    /// to support easy maintenance and system-specific adjustments.
    /// </summary>
    public class WindowerLaunchHandler : BaseLoginTaskHandler
    {
        private readonly IProcessUtilityService _processUtilityService;
        private readonly IExternalApplicationService _externalApplicationService;
        private readonly IUIAutomationService _automationService;
        private readonly IProcessLaunchService _processLaunchService;
        private readonly IMonitorPositioningService _monitorPositioningService;

        public WindowerLaunchHandler(
            ILoggingService loggingService,
            IScreenshotCaptureService screenshotService,
            ITemplateMatchingService templateService,
            ITemplateManagementService templateManagementService,
            IProcessUtilityService processUtilityService,
            IExternalApplicationService externalApplicationService,
            IUIAutomationService automationService,
            IProcessLaunchService processLaunchService,
            IMonitorPositioningService monitorPositioningService)
            : base(loggingService, screenshotService, templateService, templateManagementService)
        {
            _processUtilityService = processUtilityService ?? throw new ArgumentNullException(nameof(processUtilityService));
            _externalApplicationService = externalApplicationService ?? throw new ArgumentNullException(nameof(externalApplicationService));
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
            _processLaunchService = processLaunchService ?? throw new ArgumentNullException(nameof(processLaunchService));
            _monitorPositioningService = monitorPositioningService ?? throw new ArgumentNullException(nameof(monitorPositioningService));
        }

        public override LoginTaskStep TaskStep => LoginTaskStep.LaunchWindower;

        /// <summary>
        /// Determines if this handler can process the specified auto-login subtask.
        /// Supports all Windower-related operations from launch through UI interaction.
        /// </summary>
        /// <param name="subtask">The subtask to evaluate for handler compatibility</param>
        /// <returns>True if this handler can process the subtask, false otherwise</returns>
        /// <remarks>
        /// Supported task steps:
        /// - LaunchWindower: Application discovery and process startup
        /// - WaitForWindowerStart: Process responsiveness monitoring
        /// - VerifyWindowerLoaded: UI readiness validation
        /// - ClickLaunchButton: PlayOnline launch initiation
        /// </remarks>
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

        /// <summary>
        /// Executes the Windower application launch process with comprehensive error handling and progress reporting.
        /// This method handles the complete workflow from application discovery to process startup verification.
        /// 
        /// Process flow:
        /// 1. Validates account configuration requirements
        /// 2. Discovers Windower application using pattern-based matching
        /// 3. Checks for existing running instances (with reuse support)
        /// 4. Launches new instance if needed
        /// 5. Monitors process startup with configurable timeout
        /// 6. Obtains window handle for subsequent UI operations
        /// 
        /// Error handling:
        /// - Missing account information: Fails with descriptive message
        /// - Application not configured: Guides user to configuration
        /// - Launch failures: Provides specific failure reason
        /// - Startup timeout: Reports timeout with process detection details
        /// </summary>
        /// <param name="subtask">The subtask for progress reporting and status management</param>
        /// <param name="queueItem">The queue item containing account and configuration details</param>
        /// <param name="context">The shared context for storing process and window information</param>
        /// <param name="cancellationToken">Token for operation cancellation</param>
        /// <exception cref="InvalidOperationException">Thrown when account configuration is invalid</exception>
        /// <exception cref="TimeoutException">Thrown when process startup exceeds configured timeout</exception>
        private async Task ExecuteLaunchWindowerAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IAutoLoginContext context, CancellationToken cancellationToken)
        {

            await _loggingService.LogInfoAsync($"[FLOW] Starting ExecuteLaunchWindowerAsync for {queueItem.DisplayName}");
            subtask.Start();

            try
            {
                // Step 1: Account Validation
                // Ensure we have the minimum required account information for Windower launch
                if (string.IsNullOrEmpty(queueItem.Account?.AccountName))
                {
                    subtask.Fail("Account name is required for Windower launch");
                    return;
                }

                await UpdateProgressWithPhaseAsync(subtask, "windower", WindowerLaunchConfiguration.ProgressMilestones.ApplicationDiscovery,
                                                     "Finding Windower installation");

                // Step 2: Application Discovery
                // Use the enhanced pattern-matching service to find Windower in configured external applications
                // This replaces the previous hardcoded search logic with a more flexible, maintainable approach
                var windowerApp = await _externalApplicationService.FindApplicationByPatternAsync(
                    WindowerLaunchConfiguration.ApplicationPatterns.NamePatterns,
                    WindowerLaunchConfiguration.ApplicationPatterns.PathPatterns);

                if (windowerApp == null)
                {
                    subtask.Fail("Windower application not configured. Please add Windower to External Applications.");
                    return;
                }

                await UpdateProgressWithPhaseAsync(subtask, "windower", WindowerLaunchConfiguration.ProgressMilestones.ConfigurationValidation,
                                                     "Preparing Windower setup");

                // Step 3: Existing Process Detection
                // Check all known Windower process variations to avoid duplicate launches
                // This supports multiple Windower versions (windower, Windower4, etc.)
                var existingProcesses = await _processUtilityService.GetProcessesByNamesAsync(WindowerLaunchConfiguration.ProcessNames.WindowerVariations);
                if (existingProcesses.Any())
                {
                    // Reuse existing process - more efficient than launching new instance
                    var processId = existingProcesses.First().ProcessId;
                    await UpdateProgressWithPhaseAsync(subtask, "windower", WindowerLaunchConfiguration.ProgressMilestones.OperationComplete,
                                                         "Windower is already active");
                    context.SetData("ProcessId", processId);
                    
                    // Attempt to get window handle immediately, but don't fail if not available
                    // The window might not be ready yet, but will be detected in later steps
                    await GetWindowHandleForProcessWithRetry(context, processId, subtask, cancellationToken);
                    subtask.Complete();
                    return;
                }

                await UpdateProgressWithPhaseAsync(subtask, "windower", WindowerLaunchConfiguration.ProgressMilestones.ProcessLaunch,
                                                     "Launching Windower");

                // Launch Windower
                bool launchSuccess = await _externalApplicationService.LaunchApplicationAsync(windowerApp);
                if (!launchSuccess)
                {
                    subtask.Fail("Failed to launch Windower application");
                    return;
                }

                await UpdateProgressWithPhaseAsync(subtask, "windower", WindowerLaunchConfiguration.ProgressMilestones.ProcessStartupWait,
                                                     "Windower is starting up");

                // Step 5: Process Detection using ExternalApplicationService Integration
                // The LaunchApplicationAsync method already added the PID, so get it from the application
                // But verify the process is actually running and accessible
                int newProcessId = await _processLaunchService.GetLaunchedProcessIdAsync(
                    windowerApp,
                    subtask,
                    WindowerLaunchConfiguration.ProcessNames.WindowerVariations,
                    WindowerLaunchConfiguration.Timeouts.ProcessStartup,
                    cancellationToken);

                if (newProcessId == 0)
                {
                    subtask.Fail("Windower process did not start within timeout period");
                    return;
                }

                context.SetData("ProcessId", newProcessId);
                await UpdateProgressWithPhaseAsync(subtask, "windower", WindowerLaunchConfiguration.ProgressMilestones.WindowDetection,
                                                     "Windower application ready");

                // Verify monitor positioning - ExternalApplicationService should have already positioned Windower on primary monitor
                await _monitorPositioningService.VerifyAndCorrectMonitorPositionAsync(newProcessId, "Windower", subtask);

                // Get window handle for the process using base class method with retry logic
                await GetWindowHandleForProcessWithRetry(context, newProcessId, subtask, cancellationToken);

                await UpdateProgressWithPhaseAsync(subtask, "windower", WindowerLaunchConfiguration.ProgressMilestones.OperationComplete,
                                                     "Windower launched successfully");
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


        private async Task ExecuteWaitForWindowerStartAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IAutoLoginContext context, CancellationToken cancellationToken)
        {
            await _loggingService.LogInfoAsync($"[FLOW] Starting ExecuteWaitForWindowerStartAsync");
            subtask.Start();
            await _loggingService.LogInfoAsync($"[FLOW] ExecuteWaitForWindowerStartAsync - subtask.Start() completed");

            try
            {
                var processId = _processLaunchService.ValidateProcessIdFromContext(context, "ProcessId");
                if (processId == 0)
                {
                    subtask.Fail("No Windower process ID available from previous step");
                    return; // Validation failed
                }

                await _processLaunchService.WaitForProcessResponsivenessAsync(
                    processId,
                    subtask,
                    WindowerLaunchConfiguration.Timeouts.ProcessResponsiveness,
                    WindowerLaunchConfiguration.PollingIntervals.ProcessStartupCheck,
                    cancellationToken);
                await WaitForMainWindow(processId, context, subtask, cancellationToken);

                await UpdateProgressWithPhaseAsync(subtask, "windower", WindowerLaunchConfiguration.ProgressMilestones.StartupComplete,
                                                     "Windower interface ready");
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

        /// <summary>
        /// Waits for the Windower main window to appear and be accessible.
        /// Uses base class FindWindowHandleAsync for robust window detection with retry logic.
        /// Stores the window handle in the context when found.
        /// </summary>
        /// <param name="processId">The process ID to monitor for windows</param>
        /// <param name="context">The context to store the window handle</param>
        /// <param name="subtask">The subtask for progress reporting</param>
        /// <param name="cancellationToken">Cancellation token</param>
        private async Task WaitForMainWindow(int processId, IAutoLoginContext context, AutoLoginSubtask subtask, CancellationToken cancellationToken)
        {
            await UpdateProgressWithPhaseAsync(subtask, "windower", WindowerLaunchConfiguration.ProgressMilestones.WindowAvailability,
                                                "Windower interface loading");

            try
            {
                // Use base class method for robust window detection with retry logic
                var windowHandle = await FindWindowHandleAsync(
                    subtask,
                    WindowerLaunchConfiguration.ProcessNames.Windower,
                    "Windower", // Title filter for Windower windows
                    cancellationToken,
                    maxAttempts: (int)(WindowerLaunchConfiguration.Timeouts.WindowDetection.TotalSeconds / 
                                     WindowerLaunchConfiguration.PollingIntervals.WindowDetectionCheck.TotalSeconds));

                context.SetData("WindowHandle", windowHandle);
                await _loggingService.LogInfoAsync($"[FLOW] WaitForWindowerStart - Window handle found: 0x{windowHandle.ToInt64():X}");
            }
            catch (InvalidOperationException ex)
            {
                throw new TimeoutException($"Windower main window not found within timeout: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets window handle for a process using the base class method with retry logic.
        /// This replaces the simple custom implementation with the robust base class approach.
        /// </summary>
        /// <param name="context">The context to store the window handle</param>
        /// <param name="processId">The process ID to get window handle for</param>
        /// <param name="subtask">The subtask for progress reporting</param>
        /// <param name="cancellationToken">Cancellation token</param>
        private async Task GetWindowHandleForProcessWithRetry(IAutoLoginContext context, int processId, AutoLoginSubtask subtask, CancellationToken cancellationToken)
        {
            try
            {
                // Use base class method for robust window detection
                var windowHandle = await FindWindowHandleAsync(
                    subtask,
                    WindowerLaunchConfiguration.ProcessNames.Windower,
                    "Windower", // Title filter
                    cancellationToken,
                    maxAttempts: 5); // Quick detection for newly started process

                context.SetData("WindowHandle", windowHandle);
                await _loggingService.LogDebugAsync($"Found Windower window handle: 0x{windowHandle.ToInt64():X}");
            }
            catch (InvalidOperationException)
            {
                // Window not immediately available - this is acceptable for newly started processes
                // The window handle will be detected later in WaitForMainWindow
                await _loggingService.LogDebugAsync($"Window handle not immediately available for process {processId}, will be detected later");
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

                await UpdateProgressWithPhaseAsync(subtask, "windower", WindowerLaunchConfiguration.ProgressMilestones.UIInitialization,
                                                     "Preparing Windower interface");

                // Wait for UI to stabilize before detection
                await Task.Delay(WindowerLaunchConfiguration.Timeouts.UIStabilization, cancellationToken);

                // Use standardized detection with standard confidence threshold
                var launchArrowMatch = await WaitForScreenDetectionAsync(
                    subtask,
                    WindowerLaunchConfiguration.TemplatePaths.LaunchArrow,
                    windowHandle,
                    "Windower launch arrow",
                    cancellationToken,
                    ScreenDetectionOptions.Default);

                if (launchArrowMatch.Confidence < WindowerLaunchConfiguration.ConfidenceThresholds.LaunchArrowDetection)
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

                await UpdateProgressWithPhaseAsync(subtask, "windower", WindowerLaunchConfiguration.ProgressMilestones.WindowFocus,
                                                     "Activating Windower window");

                // Ensure window focus
                await _automationService.EnsureWindowFocusAsync(windowHandle, cancellationToken);
                await Task.Delay(WindowerLaunchConfiguration.Timeouts.WindowFocusDelay, cancellationToken);

                await UpdateProgressWithPhaseAsync(subtask, "windower", WindowerLaunchConfiguration.ProgressMilestones.ButtonDetection,
                                                     "Finding launch controls");

                // Find the launch arrow using standardized detection
                var launchArrowMatch = await WaitForScreenDetectionAsync(
                    subtask,
                    WindowerLaunchConfiguration.TemplatePaths.LaunchArrow,
                    windowHandle,
                    "launch arrow button",
                    cancellationToken,
                    ScreenDetectionOptions.Quick); // 10-second timeout for button detection

                if (launchArrowMatch.Confidence < WindowerLaunchConfiguration.ConfidenceThresholds.LaunchArrowDetection)
                {
                    throw new InvalidOperationException($"Could not detect launch arrow button (confidence: {launchArrowMatch.Confidence:P})");
                }

                // Use template-driven navigation (relative click anchored to detected template)
                var navSuccess = await ExecuteNavigationFromTemplateAsync(
                    subtask,
                    WindowerLaunchConfiguration.TemplatePaths.LaunchArrow,
                    windowHandle,
                    launchArrowMatch,
                    _automationService,
                    cancellationToken);

                if (!navSuccess)
                {
                    throw new InvalidOperationException("Failed to activate Windower launch button");
                }

                await UpdateProgressWithPhaseAsync(subtask, "windower", WindowerLaunchConfiguration.ProgressMilestones.ButtonClick,
                                                     "Starting PlayOnline launcher");

                // Wait for PlayOnline process to start
                await Task.Delay(WindowerLaunchConfiguration.Timeouts.PostLaunchDelay, cancellationToken);

                var polProcessId = await _processLaunchService.WaitForProcessStartupAsync(
                    new[] { WindowerLaunchConfiguration.ProcessNames.PlayOnline },
                    subtask,
                    WindowerLaunchConfiguration.Timeouts.PlayOnlineStartup,
                    WindowerLaunchConfiguration.PollingIntervals.PlayOnlineDetectionCheck,
                    cancellationToken);

                if (polProcessId > 0)
                {
                    await UpdateProgressWithPhaseAsync(subtask, "windower", WindowerLaunchConfiguration.ProgressMilestones.PostLaunchWait,
                                                         "PlayOnline is starting up");
                    await _loggingService.LogDebugAsync($"PlayOnline process detected: PID {polProcessId}");
                }
                else
                {
                    // Even if we don't detect POL immediately, the click was successful
                    await UpdateProgressWithPhaseAsync(subtask, "windower", WindowerLaunchConfiguration.ProgressMilestones.OperationComplete,
                                                         "Transitioning to PlayOnline");
                }

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
    }
}
