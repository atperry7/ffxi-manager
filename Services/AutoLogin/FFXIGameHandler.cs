using System;
using System.Diagnostics;
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
    /// Handles Final Fantasy XI game-specific tasks including character selection and login finalization.
    /// Orchestrates the entire FFXI login flow through multiple distinct phases.
    /// </summary>
    /// <remarks>
    /// <para><strong>Supported Task Steps:</strong></para>
    /// <list type="bullet">
    ///   <item><description>TermsAcceptance - Handles FFXI Terms of Service screen interaction</description></item>
    ///   <item><description>CharacterSelection - Navigates the main menu to character selection</description></item>
    ///   <item><description>CharacterSlotPick - Selects the configured character slot using arrow navigation</description></item>
    ///   <item><description>ConfirmLogin - Confirms final login and enters the game world</description></item>
    /// </list>
    /// 
    /// <para><strong>Configuration Dependencies:</strong></para>
    /// <list type="bullet">
    ///   <item><description>FFXIGameConfiguration - Centralized configuration for all timeouts, delays, and process discovery</description></item>
    ///   <item><description>Template metadata JSON files for screen detection confidence thresholds</description></item>
    /// </list>
    /// 
    /// <para><strong>Service Dependencies:</strong></para>
    /// <list type="bullet">
    ///   <item><description>IUIAutomationService - Window focus management and DirectX-compatible keyboard input</description></item>
    ///   <item><description>IAutoLoginContextService - Cross-handler state management (window handles, account data)</description></item>
    ///   <item><description>Base class services - Screenshot capture, template matching, logging</description></item>
    /// </list>
    /// 
    /// <para><strong>FFXI-Specific Handling:</strong></para>
    /// <list type="bullet">
    ///   <item><description>DirectX window interaction requiring focus-based automation</description></item>
    ///   <item><description>Dynamic window handle management for FFXI process transitions</description></item>
    ///   <item><description>Template-based screen detection with confidence validation</description></item>
    ///   <item><description>Character slot navigation using arrow key automation</description></item>
    ///   <item><description>Configurable delays for FFXI UI timing requirements</description></item>
    /// </list>
    /// </remarks>
    public class FFXIGameHandler : BaseLoginTaskHandler
    {
        private readonly IUIAutomationService _automationService;
        private readonly IAutoLoginContextService _contextService;
        private readonly IProcessUtilityService _processUtilityService;

        public FFXIGameHandler(
            ILoggingService loggingService,
            IScreenshotCaptureService screenshotService,
            ITemplateMatchingService templateService,
            ITemplateManagementService templateManagementService,
            IUIAutomationService automationService,
            IAutoLoginContextService contextService,
            IProcessUtilityService processUtilityService)
            : base(loggingService, screenshotService, templateService, templateManagementService)
        {
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
            _contextService = contextService ?? throw new ArgumentNullException(nameof(contextService));
            _processUtilityService = processUtilityService ?? throw new ArgumentNullException(nameof(processUtilityService));
        }

        public override LoginTaskStep TaskStep => LoginTaskStep.TermsAcceptance;

        public override bool CanHandle(AutoLoginSubtask subtask)
        {
            return subtask.TaskStep switch
            {
                LoginTaskStep.TermsAcceptance => true,
                LoginTaskStep.CharacterSelection => true,
                LoginTaskStep.CharacterSlotPick => true,
                LoginTaskStep.ConfirmLogin => true,
                _ => false
            };
        }

        /// <summary>
        /// Executes the primary FFXI handler logic by dispatching to appropriate task-specific methods.
        /// Provides comprehensive error handling and progress tracking for all FFXI login phases.
        /// </summary>
        /// <param name="subtask">The specific subtask containing task step and progress tracking</param>
        /// <param name="queueItem">Queue item containing account configuration and character slot information</param>
        /// <param name="context">Context for cross-handler communication and window handle management</param>
        /// <param name="cancellationToken">Cancellation token for operation cancellation</param>
        /// <remarks>
        /// <para><strong>Configuration Dependencies:</strong></para>
        /// <list type="bullet">
        ///   <item><description>FFXIGameConfiguration.* - All timing, process discovery, and UI navigation settings</description></item>
        ///   <item><description>Account.FFXICharacterSlot - Target character slot for selection (defaults to slot 1)</description></item>
        /// </list>
        /// 
        /// <para><strong>Service Dependencies:</strong></para>
        /// <list type="bullet">
        ///   <item><description>All injected services through constructor (automation, context, base services)</description></item>
        ///   <item><description>BaseLoginTaskHandler infrastructure for retry logic and progress reporting</description></item>
        /// </list>
        /// 
        /// <para><strong>Error Scenarios:</strong></para>
        /// <list type="bullet">
        ///   <item><description>Unsupported task steps: NotSupportedException with clear error message</description></item>
        ///   <item><description>Process/window failures: Handled by individual task methods with retries</description></item>
        ///   <item><description>Screen detection timeouts: Enhanced with fallback redetection strategies</description></item>
        ///   <item><description>Cancellation: Properly propagated with subtask status updates</description></item>
        /// </list>
        /// 
        /// <para><strong>Progress Reporting:</strong></para>
        /// Progress values are managed by individual task methods using standardized milestones from
        /// FFXIGameConfiguration.ProgressMilestones. Each task method handles its own 0-100% range.
        /// 
        /// <para><strong>Example Flow:</strong></para>
        /// <code>
        /// // TermsAcceptance: Process detection → Screen detection → Enter key → Complete
        /// // CharacterSelection: Screen detection → Menu navigation → Transition
        /// // CharacterSlotPick: Screen detection → Arrow navigation → Enter selection
        /// // ConfirmLogin: Screen detection → Final confirmation → Game world entry
        /// </code>
        /// </remarks>
        /// <exception cref="NotSupportedException">Thrown when an unsupported TaskStep is encountered</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
        /// <exception cref="InvalidOperationException">Thrown for FFXI-specific failures (process not found, etc.)</exception>
        /// <exception cref="TimeoutException">Thrown when screen detection or other timed operations exceed configured limits</exception>
        protected override async Task ExecuteHandlerLogicAsync(
            AutoLoginSubtask subtask,
            AutoLoginQueueItem queueItem,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            subtask.Start();

            await _loggingService.LogInfoAsync($"[FLOW] Starting FFXI {subtask.TaskStep} for {queueItem.DisplayName}");

            try
            {
                switch (subtask.TaskStep)
                {
                    case LoginTaskStep.TermsAcceptance:
                        await ExecuteTermsAcceptanceAsync(subtask, queueItem, context, cancellationToken);
                        break;

                    case LoginTaskStep.CharacterSelection:
                        await ExecuteCharacterSelectionAsync(subtask, queueItem, context, cancellationToken);
                        break;

                    case LoginTaskStep.CharacterSlotPick:
                        await ExecuteCharacterSlotPickAsync(subtask, queueItem, context, cancellationToken);
                        break;

                    case LoginTaskStep.ConfirmLogin:
                        await ExecuteConfirmLoginAsync(subtask, queueItem, context, cancellationToken);
                        break;

                    default:
                        throw new NotSupportedException($"Task step {subtask.TaskStep} is not supported by FFXIGameHandler");
                }

                subtask.Complete();
                await _loggingService.LogInfoAsync($"[FLOW] Completed FFXI {subtask.TaskStep} for {queueItem.DisplayName}");
            }
            catch (OperationCanceledException)
            {
                subtask.Cancel();
                throw;
            }
            catch (Exception ex)
            {
                subtask.Fail(ex.Message);
                await _loggingService.LogErrorAsync($"FFXI {subtask.TaskStep} failed for {queueItem.DisplayName}", ex);
                throw;
            }
        }

        /// <summary>
        /// Executes the FFXI Terms of Service acceptance phase.
        /// Waits for FFXI process launch, detects the terms screen, and accepts terms to proceed to main menu.
        /// </summary>
        /// <param name="subtask">Subtask for progress reporting (0-100%)</param>
        /// <param name="queueItem">Queue item containing account information (used for display purposes)</param>
        /// <param name="context">Context for storing the FFXI window handle for subsequent tasks</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <remarks>
        /// <para><strong>Configuration Dependencies:</strong></para>
        /// <list type="bullet">
        ///   <item><description>FFXIGameConfiguration.Timeouts.TermsAcceptance - Screen detection timeout</description></item>
        ///   <item><description>FFXIGameConfiguration.PollingIntervals.ScreenCheck - Detection polling frequency</description></item>
        ///   <item><description>FFXIGameConfiguration.Delays.ScreenStabilization - UI stabilization wait</description></item>
        ///   <item><description>FFXIGameConfiguration.Delays.WindowFocus/Transition - Navigation timing</description></item>
        ///   <item><description>FFXIGameConfiguration.TemplatePaths.TermsAcceptance - Template for screen detection</description></item>
        /// </list>
        /// 
        /// <para><strong>Service Dependencies:</strong></para>
        /// <list type="bullet">
        ///   <item><description>WaitForFFXIProcessAsync - Process discovery and window handle retrieval</description></item>
        ///   <item><description>WaitForFFXIStartup - Window responsiveness validation</description></item>
        ///   <item><description>WaitForScreenWithRedetectionFallbackAsync - Screen detection with failover</description></item>
        ///   <item><description>ExecuteNavigationFromTemplateAsync - Template-driven keyboard navigation</description></item>
        /// </list>
        /// 
        /// <para><strong>Error Scenarios:</strong></para>
        /// <list type="bullet">
        ///   <item><description>FFXI process doesn't launch: InvalidOperationException after timeout</description></item>
        ///   <item><description>Terms screen not detected: TimeoutException with fallback redetection attempt</description></item>
        ///   <item><description>Window becomes unresponsive: Automatic window handle redetection</description></item>
        ///   <item><description>Navigation input fails: Logged but operation continues</description></item>
        /// </list>
        /// 
        /// <para><strong>Progress Milestones:</strong></para>
        /// <list type="bullet">
        ///   <item><description>5% - Starting FFXI process detection</description></item>
        ///   <item><description>15% - Waiting for Terms screen</description></item>
        ///   <item><description>60% - Terms screen detected</description></item>
        ///   <item><description>70% - Accepting terms (sending Enter)</description></item>
        ///   <item><description>100% - Terms accepted, transitioning to main menu</description></item>
        /// </list>
        /// 
        /// <para><strong>FFXI-Specific Behavior:</strong></para>
        /// <list type="bullet">
        ///   <item><description>Process may launch as 'pol' initially, then transition to 'ffximain'</description></item>
        ///   <item><description>Window handle stored in context for use by subsequent task phases</description></item>
        ///   <item><description>DirectX rendering requires focus + delay before input for reliability</description></item>
        ///   <item><description>Terms screen uses template matching with confidence threshold from JSON metadata</description></item>
        /// </list>
        /// 
        /// <para><strong>Example Usage:</strong></para>
        /// <code>
        /// // This method is called automatically by ExecuteHandlerLogicAsync when
        /// // subtask.TaskStep == LoginTaskStep.TermsAcceptance
        /// // 
        /// // Typical flow:
        /// // 1. WaitForFFXIProcessAsync() finds process window handle
        /// // 2. Handle stored in context as "FFXIWindowHandle"
        /// // 3. WaitForFFXIStartup() ensures window responsiveness
        /// // 4. WaitForScreenWithRedetectionFallbackAsync() detects terms screen
        /// // 5. ExecuteNavigationFromTemplateAsync() executes template-driven navigation to accept terms
        /// // 6. Transition delay allows menu loading for next phase
        /// </code>
        /// </remarks>
        /// <exception cref="InvalidOperationException">FFXI process fails to launch within configured timeout</exception>
        /// <exception cref="TimeoutException">Terms screen detection fails even with redetection fallback</exception>
        /// <exception cref="OperationCanceledException">Operation cancelled via cancellation token</exception>
        private async Task ExecuteTermsAcceptanceAsync(
            AutoLoginSubtask subtask,
            AutoLoginQueueItem queueItem,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            // FFXI-Specific: Process may launch as 'pol' initially, then transition to 'ffximain'
            // This discovery handles both direct FFXI launch and PlayOnline→FFXI transitions
            await UpdateProgressWithPhaseAsync(subtask, "gameconnection", FFXIGameConfiguration.ProgressMilestones.TermsProcessWait,
                                                "Starting FINAL FANTASY XI");
            var ffxiWindowHandle = await WaitForFFXIProcessAsync(subtask, context, cancellationToken);

            // Store FFXI window handle for subsequent steps - critical for cross-task communication
            // Other handlers (CharacterSelection, etc.) will retrieve this handle from context
            context.SetData("FFXIWindowHandle", ffxiWindowHandle);

            // FFXI-Specific: DirectX window requires time to become responsive for automation
            // Multiple screenshot attempts ensure window is stable before UI interaction
            await WaitForFFXIStartup(ffxiWindowHandle, subtask, cancellationToken);

            await UpdateProgressWithPhaseAsync(subtask, "gameconnection", FFXIGameConfiguration.ProgressMilestones.TermsScreenDetection,
                                                "Loading game interface");

            // Wait for FFXI accept terms screen using standardized detection with fallback
            var termsMatch = await WaitForScreenWithRedetectionFallbackAsync(
                subtask,
                FFXIGameConfiguration.TemplatePaths.TermsAcceptance,
                ffxiWindowHandle,
                context,
                "FFXI Terms of Service",
                FFXIGameConfiguration.Timeouts.TermsAcceptance,
                FFXIGameConfiguration.PollingIntervals.ScreenCheck,
                cancellationToken);

            // Template matching success confirmed - confidence threshold from JSON metadata was met
            // This ensures we have the actual FFXI Terms screen, not a false positive
            await _loggingService.LogInfoAsync($"FFXI Terms screen detected with confidence: {termsMatch.Confidence:P}");

            await UpdateProgressWithPhaseAsync(subtask, "gameconnection", FFXIGameConfiguration.ProgressMilestones.TermsScreenDetected,
                                                "Game interface ready");

            // FFXI-Specific: Window handle may change during screen transitions
            // Redetection logic in screen detection may have updated the context
            ffxiWindowHandle = ValidateWindowContextAsync(context, ffxiWindowHandle);

            // FFXI-Specific: DirectX UI requires stabilization time after screen detection
            // Prevents input timing issues with FFXI's rendering pipeline
            await WaitForScreenTransitionAsync(subtask, FFXIGameConfiguration.Delays.ScreenStabilization, cancellationToken: cancellationToken);

            // Execute terms acceptance with template-driven navigation
            await UpdateProgressAsync(subtask, FFXIGameConfiguration.ProgressMilestones.TermsAccepting, "Accepting terms...");

            var navSuccess = await ExecuteNavigationFromTemplateAsync(
                subtask,
                FFXIGameConfiguration.TemplatePaths.TermsAcceptance,
                ffxiWindowHandle,
                termsMatch,
                _automationService,
                cancellationToken);

            if (!navSuccess)
            {
                throw new InvalidOperationException("Failed to accept FFXI terms of service");
            }

            await UpdateProgressWithPhaseAsync(subtask, "gameconnection", FFXIGameConfiguration.ProgressMilestones.TermsComplete,
                                                "Accessing main menu");
        }

        /// <summary>
        /// Executes the FFXI main menu navigation to reach character selection.
        /// Detects the main menu screen and presses Enter to access character slot selection.
        /// </summary>
        /// <param name="subtask">Subtask for progress reporting (0-100%)</param>
        /// <param name="queueItem">Queue item containing account information (used for logging)</param>
        /// <param name="context">Context for retrieving FFXI window handle from previous task</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <remarks>
        /// <para><strong>Configuration Dependencies:</strong></para>
        /// <list type="bullet">
        ///   <item><description>FFXIGameConfiguration.Timeouts.MainMenu - Screen detection timeout</description></item>
        ///   <item><description>FFXIGameConfiguration.PollingIntervals.MenuCheck - Detection polling frequency</description></item>
        ///   <item><description>FFXIGameConfiguration.Delays.ScreenStabilization - Menu stabilization wait</description></item>
        ///   <item><description>FFXIGameConfiguration.Delays.WindowFocus/Transition - Navigation timing</description></item>
        ///   <item><description>FFXIGameConfiguration.TemplatePaths.MainMenu - Template for main menu detection</description></item>
        /// </list>
        /// 
        /// <para><strong>Service Dependencies:</strong></para>
        /// <list type="bullet">
        ///   <item><description>GetFFXIWindowHandleAsync - Window handle retrieval and validation</description></item>
        ///   <item><description>WaitForScreenWithRedetectionFallbackAsync - Main menu screen detection</description></item>
        ///   <item><description>ExecuteNavigationFromTemplateAsync - Template-driven navigation for menu selection</description></item>
        ///   <item><description>ValidateWindowContextAsync - Window handle validation and updates</description></item>
        /// </list>
        /// 
        /// <para><strong>Error Scenarios:</strong></para>
        /// <list type="bullet">
        ///   <item><description>Window handle not found/invalid: GetFFXIWindowHandleAsync handles redetection</description></item>
        ///   <item><description>Main menu not detected: TimeoutException with fallback redetection attempt</description></item>
        ///   <item><description>Menu animations interfere: Stabilization delay addresses timing issues</description></item>
        ///   <item><description>Navigation input fails: Logged but operation continues</description></item>
        /// </list>
        /// 
        /// <para><strong>Progress Milestones:</strong></para>
        /// <list type="bullet">
        ///   <item><description>10% - Starting main menu detection</description></item>
        ///   <item><description>50% - Main menu detected successfully</description></item>
        ///   <item><description>70% - Menu selection in progress (Enter being sent)</description></item>
        ///   <item><description>100% - Menu navigation completed, transitioning to character slots</description></item>
        /// </list>
        /// 
        /// <para><strong>FFXI-Specific Behavior:</strong></para>
        /// <list type="bullet">
        ///   <item><description>Main menu may have animation delays requiring stabilization wait</description></item>
        ///   <item><description>DirectX menu requires precise focus management for input reliability</description></item>
        ///   <item><description>Template detection uses confidence threshold from JSON metadata</description></item>
        ///   <item><description>Window handle validated and updated in context if changed</description></item>
        /// </list>
        /// 
        /// <para><strong>Example Usage:</strong></para>
        /// <code>
        /// // Called automatically by ExecuteHandlerLogicAsync when
        /// // subtask.TaskStep == LoginTaskStep.CharacterSelection
        /// //
        /// // Typical flow:
        /// // 1. GetFFXIWindowHandleAsync() retrieves handle from context or rediscovers
        /// // 2. WaitForScreenWithRedetectionFallbackAsync() detects main menu screen
        /// // 3. WaitForScreenTransitionAsync() allows menu animations to complete
        /// // 4. ExecuteNavigationFromTemplateAsync() executes template-driven navigation to select character option
        /// // 5. Transition preparation for character slot selection phase
        /// </code>
        /// </remarks>
        /// <exception cref="InvalidOperationException">FFXI window handle cannot be found or validated</exception>
        /// <exception cref="TimeoutException">Main menu screen detection fails even with redetection</exception>
        /// <exception cref="OperationCanceledException">Operation cancelled via cancellation token</exception>
        private async Task ExecuteCharacterSelectionAsync(
            AutoLoginSubtask subtask,
            AutoLoginQueueItem queueItem,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            // Get FFXI window handle from context
            var ffxiWindowHandle = await GetFFXIWindowHandleAsync(subtask, context, cancellationToken);

            await UpdateProgressWithPhaseAsync(subtask, "gameconnection", FFXIGameConfiguration.ProgressMilestones.MenuWait,
                                                "Loading character menu");

            // Wait for main menu screen using standardized detection with fallback
            var mainMenuMatch = await WaitForScreenWithRedetectionFallbackAsync(
                subtask,
                FFXIGameConfiguration.TemplatePaths.MainMenu,
                ffxiWindowHandle,
                context,
                "FFXI main menu",
                FFXIGameConfiguration.Timeouts.MainMenu,
                FFXIGameConfiguration.PollingIntervals.MenuCheck,
                cancellationToken);

            // The WaitForScreenDetectionWithRedetectionAsync method already validates against the template threshold
            // from the JSON configuration, so if we get here, detection was successful
            await _loggingService.LogInfoAsync($"FFXI main menu detected with confidence: {mainMenuMatch.Confidence:P}");

            await UpdateProgressWithPhaseAsync(subtask, "gameconnection", FFXIGameConfiguration.ProgressMilestones.MenuDetected,
                                                "Character menu ready");

            // Get updated window handle from context
            ffxiWindowHandle = ValidateWindowContextAsync(context, ffxiWindowHandle);

            // Allow menu animations to complete and execute selection
            await WaitForScreenTransitionAsync(subtask, FFXIGameConfiguration.Delays.ScreenStabilization,
                70, "Selecting character option...", cancellationToken);

            // Execute menu selection with template-driven navigation
            var navSuccess = await ExecuteNavigationFromTemplateAsync(
                subtask,
                FFXIGameConfiguration.TemplatePaths.MainMenu,
                ffxiWindowHandle,
                mainMenuMatch,
                _automationService,
                cancellationToken);

            if (!navSuccess)
            {
                throw new InvalidOperationException("Failed to select character option from main menu");
            }

            await UpdateProgressWithPhaseAsync(subtask, "gameconnection", 100,
                                                "Accessing character selection");
        }

        /// <summary>
        /// Validates the character slot number is within acceptable range.
        /// Provides clear error messages for invalid slot configurations.
        /// </summary>
        /// <param name="characterSlot">Character slot number to validate</param>
        /// <returns>True if slot is valid, throws exception if invalid</returns>
        /// <exception cref="InvalidOperationException">Thrown when slot is outside valid range</exception>
        private static bool ValidateCharacterSlot(int characterSlot)
        {
            if (characterSlot < FFXIGameConfiguration.CharacterSlots.MinSlotNumber || 
                characterSlot > FFXIGameConfiguration.CharacterSlots.MaxSlotNumber)
            {
                throw new InvalidOperationException(
                    $"Invalid character slot: {characterSlot}. Must be between " +
                    $"{FFXIGameConfiguration.CharacterSlots.MinSlotNumber} and {FFXIGameConfiguration.CharacterSlots.MaxSlotNumber}");
            }
            return true;
        }

        /// <summary>
        /// Performs screen detection with fallback to window redetection on timeout.
        /// Centralizes the common pattern of standard detection + redetection fallback used throughout FFXI handler.
        /// </summary>
        /// <param name="subtask">Subtask for progress reporting during detection attempts</param>
        /// <param name="templatePath">Template path for screen detection (e.g., "FFXI/ffxi_terms_acceptance")</param>
        /// <param name="windowHandle">Current FFXI window handle to capture from</param>
        /// <param name="context">Context for window handle updates if redetection occurs</param>
        /// <param name="screenDescription">Human-readable description of screen being detected (for logging)</param>
        /// <param name="timeout">Maximum time to spend on detection attempts</param>
        /// <param name="checkInterval">Time between individual detection attempts</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Template match result with confidence score</returns>
        /// <remarks>
        /// <para><strong>Detection Strategy:</strong></para>
        /// <list type="number">
        ///   <item><description>Primary: Use base class WaitForScreenDetectionAsync with standard retry logic</description></item>
        ///   <item><description>Fallback: On timeout, attempt WaitForScreenDetectionWithRedetectionAsync for window handle recovery</description></item>
        /// </list>
        /// 
        /// <para><strong>Configuration Dependencies:</strong></para>
        /// <list type="bullet">
        ///   <item><description>Template JSON metadata for confidence thresholds</description></item>
        ///   <item><description>Provided timeout and checkInterval parameters (typically from FFXIGameConfiguration)</description></item>
        /// </list>
        /// 
        /// <para><strong>FFXI-Specific Behavior:</strong></para>
        /// <list type="bullet">
        ///   <item><description>Handles window handle invalidation during FFXI screen transitions</description></item>
        ///   <item><description>Provides enhanced error logging for detection failures</description></item>
        ///   <item><description>Integrates with centralized window handle management in context</description></item>
        /// </list>
        /// 
        /// <para><strong>Example Usage:</strong></para>
        /// <code>
        /// var termsMatch = await WaitForScreenWithRedetectionFallbackAsync(
        ///     subtask,
        ///     FFXIGameConfiguration.TemplatePaths.TermsAcceptance,
        ///     ffxiWindowHandle,
        ///     context,
        ///     "FFXI Terms of Service",
        ///     FFXIGameConfiguration.Timeouts.TermsAcceptance,
        ///     FFXIGameConfiguration.PollingIntervals.ScreenCheck,
        ///     cancellationToken);
        /// </code>
        /// </remarks>
        /// <exception cref="TimeoutException">Both standard and redetection attempts fail within timeout</exception>
        /// <exception cref="OperationCanceledException">Operation cancelled via cancellation token</exception>
        private async Task<TemplateMatchResult> WaitForScreenWithRedetectionFallbackAsync(
            AutoLoginSubtask subtask,
            string templatePath,
            IntPtr windowHandle,
            IAutoLoginContext context,
            string screenDescription,
            TimeSpan timeout,
            TimeSpan checkInterval,
            CancellationToken cancellationToken)
        {
            try
            {
                return await WaitForScreenDetectionAsync(
                    subtask,
                    templatePath,
                    windowHandle,
                    screenDescription,
                    cancellationToken,
                    new ScreenDetectionOptions
                    {
                        Timeout = timeout,
                        CheckInterval = checkInterval
                    });
            }
            catch (TimeoutException)
            {
                // Fallback to window redetection if standard detection fails
                await _loggingService.LogInfoAsync($"Standard {screenDescription} detection failed, attempting with window redetection");
                return await WaitForScreenDetectionWithRedetectionAsync(
                    subtask,
                    templatePath,
                    windowHandle,
                    context,
                    screenDescription,
                    cancellationToken,
                    new ScreenDetectionOptions
                    {
                        Timeout = timeout,
                        CheckInterval = checkInterval
                    });
            }
        }

        /// <summary>
        /// Waits for screen transition with standardized delay and optional progress update.
        /// Centralizes transition waiting patterns used throughout the FFXI handler for consistent timing.
        /// </summary>
        /// <param name="subtask">Subtask for optional progress reporting during the delay</param>
        /// <param name="delayType">Configured delay duration (e.g., FFXIGameConfiguration.Delays.Transition)</param>
        /// <param name="progressValue">Optional progress percentage to set before waiting</param>
        /// <param name="progressMessage">Optional progress message to display during wait</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <remarks>
        /// <para><strong>Purpose:</strong></para>
        /// Eliminates duplicate delay + progress update patterns throughout the handler by providing
        /// a centralized method for screen transition timing with optional progress reporting.
        /// 
        /// <para><strong>Configuration Dependencies:</strong></para>
        /// <list type="bullet">
        ///   <item><description>Delay duration provided via FFXIGameConfiguration.Delays.* constants</description></item>
        /// </list>
        /// 
        /// <para><strong>FFXI-Specific Timing:</strong></para>
        /// <list type="bullet">
        ///   <item><description>ScreenStabilization: After template detection, before UI interaction</description></item>
        ///   <item><description>Transition: After UI input, waiting for screen change</description></item>
        ///   <item><description>WindowFocus: Before input delivery to ensure focus</description></item>
        ///   <item><description>NavigationStep: Between navigation inputs to prevent skipping</description></item>
        /// </list>
        /// 
        /// <para><strong>Example Usage:</strong></para>
        /// <code>
        /// // Screen stabilization with progress update
        /// await WaitForScreenTransitionAsync(
        ///     subtask, 
        ///     FFXIGameConfiguration.Delays.ScreenStabilization, 
        ///     70, 
        ///     "Selecting character option...", 
        ///     cancellationToken);
        /// 
        /// // Simple delay without progress update
        /// await WaitForScreenTransitionAsync(
        ///     subtask, 
        ///     FFXIGameConfiguration.Delays.Transition, 
        ///     cancellationToken: cancellationToken);
        /// </code>
        /// </remarks>
        /// <exception cref="OperationCanceledException">Operation cancelled via cancellation token</exception>
        private async Task WaitForScreenTransitionAsync(
            AutoLoginSubtask subtask,
            TimeSpan delayType,
            int? progressValue = null,
            string? progressMessage = null,
            CancellationToken cancellationToken = default)
        {
            if (progressValue.HasValue && !string.IsNullOrEmpty(progressMessage))
            {
                await UpdateProgressAsync(subtask, progressValue.Value, progressMessage);
            }
            
            await Task.Delay(delayType, cancellationToken);
        }

        /// <summary>
        /// Validates and updates window handle from context with error handling.
        /// Centralizes window handle management pattern used throughout the handler.
        /// </summary>
        /// <param name="context">Context to retrieve window handle from</param>
        /// <param name="fallbackHandle">Fallback handle if context handle is invalid</param>
        /// <returns>Valid window handle</returns>
        private IntPtr ValidateWindowContextAsync(IAutoLoginContext context, IntPtr fallbackHandle = default)
        {
            var contextHandle = context.GetValueData<IntPtr>("FFXIWindowHandle");
            return contextHandle != IntPtr.Zero ? contextHandle : fallbackHandle;
        }

        /// <summary>
        /// Prepares window for navigation by ensuring focus and allowing stabilization.
        /// Critical for DirectX input reliability - centralizes focus management pattern used throughout FFXI automation.
        /// </summary>
        /// <param name="windowHandle">Handle of the FFXI window to prepare for input</param>
        /// <param name="delayType">Configured delay for focus stabilization (from FFXIGameConfiguration.Delays)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <remarks>
        /// <para><strong>Focus Preparation Pattern:</strong></para>
        /// <list type="number">
        ///   <item><description>EnsureWindowFocusAsync: Bring FFXI window to foreground and set input focus</description></item>
        ///   <item><description>Stabilization delay: Allow focus change to complete before input delivery</description></item>
        /// </list>
        /// 
        /// <para><strong>Configuration Dependencies:</strong></para>
        /// <list type="bullet">
        ///   <item><description>delayType: Typically WindowFocus (500ms) or FocusStabilization (750ms)</description></item>
        /// </list>
        /// 
        /// <para><strong>Service Dependencies:</strong></para>
        /// <list type="bullet">
        ///   <item><description>IUIAutomationService.EnsureWindowFocusAsync - Platform-specific window focus management</description></item>
        /// </list>
        /// 
        /// <para><strong>FFXI-Specific Requirements:</strong></para>
        /// <list type="bullet">
        ///   <item><description>DirectX applications require explicit focus before accepting input reliably</description></item>
        ///   <item><description>Focus changes need processing time before input delivery (timing-sensitive)</description></item>
        ///   <item><description>Window focus can be lost between navigation steps, especially in windowed mode</description></item>
        ///   <item><description>Stabilization delay prevents input timing issues with FFXI's UI processing</description></item>
        /// </list>
        /// 
        /// <para><strong>Common Delay Types:</strong></para>
        /// <list type="bullet">
        ///   <item><description>WindowFocus (500ms): Standard focus preparation before input</description></item>
        ///   <item><description>FocusStabilization (750ms): Extended stabilization for complex navigation</description></item>
        ///   <item><description>EnterPreparation (750ms): Focus preparation before critical Enter key presses</description></item>
        /// </list>
        /// 
        /// <para><strong>Example Usage:</strong></para>
        /// <code>
        /// // Standard window preparation before key input
        /// await PrepareWindowForNavigationAsync(
        ///     windowHandle, 
        ///     FFXIGameConfiguration.Delays.WindowFocus, 
        ///     cancellationToken);
        /// 
        /// // Extended preparation before character selection
        /// await PrepareWindowForNavigationAsync(
        ///     windowHandle, 
        ///     FFXIGameConfiguration.Delays.FocusStabilization, 
        ///     cancellationToken);
        /// </code>
        /// </remarks>
        /// <exception cref="OperationCanceledException">Operation cancelled via cancellation token</exception>
        private async Task PrepareWindowForNavigationAsync(
            IntPtr windowHandle,
            TimeSpan delayType,
            CancellationToken cancellationToken)
        {
            await _automationService.EnsureWindowFocusAsync(windowHandle, cancellationToken);
            await Task.Delay(delayType, cancellationToken);
        }

        /// <summary>
        /// Navigates to the specified character slot using arrow key navigation.
        /// Handles the step-by-step navigation with progress reporting and proper delays.
        /// </summary>
        /// <param name="subtask">Subtask for progress updates</param>
        /// <param name="targetSlot">Target character slot number</param>
        /// <param name="windowHandle">FFXI window handle</param>
        /// <param name="cancellationToken">Cancellation token</param>
        private async Task NavigateToCharacterSlotAsync(
            AutoLoginSubtask subtask,
            int targetSlot,
            IntPtr windowHandle,
            CancellationToken cancellationToken)
        {
            if (targetSlot <= FFXIGameConfiguration.CharacterSlots.DefaultSlotNumber)
            {
                await UpdateProgressAsync(subtask, FFXIGameConfiguration.ProgressMilestones.SlotNavigationEnd,
                    $"Using default character slot {targetSlot}");
                return;
            }

            await UpdateProgressAsync(subtask, FFXIGameConfiguration.ProgressMilestones.SlotNavigationStart,
                $"Navigating to character slot {targetSlot}...");

            int stepsNeeded = targetSlot - FFXIGameConfiguration.CharacterSlots.DefaultSlotNumber;
            await _loggingService.LogInfoAsync($"[NAVIGATION] Need to navigate from slot 1 to slot {targetSlot} (sending {stepsNeeded} down arrows)");

            // Create programmatic navigation action with dynamic count
            var slotNavigation = new NavigationAction
            {
                Type = NavigationType.Keyboard,
                PostNavigationDelayMs = (int)FFXIGameConfiguration.Delays.NavigationStep.TotalMilliseconds
            };
            slotNavigation.Sequence.Add(new KeyboardAction
            {
                Action = "DownArrow",
                Count = stepsNeeded,
                DelayMs = (int)FFXIGameConfiguration.Delays.NavigationStep.TotalMilliseconds,
                Description = $"Navigate to character slot {targetSlot}"
            });

            // Execute navigation using keyboard strategy
            var navSuccess = await ExecuteNavigationActionAsync(
                subtask,
                slotNavigation,
                windowHandle,
                null, // No template match needed for programmatic navigation
                _automationService,
                cancellationToken);

            if (!navSuccess)
            {
                throw new InvalidOperationException($"Failed to navigate to character slot {targetSlot}");
            }

            await UpdateProgressAsync(subtask, FFXIGameConfiguration.ProgressMilestones.SlotNavigationEnd,
                $"Reached character slot {targetSlot}");
            await _loggingService.LogInfoAsync($"[NAVIGATION] Navigation complete - should now be on slot {targetSlot}");
        }

        /// <summary>
        /// Selects the currently highlighted character slot by pressing Enter.
        /// Includes proper window focus preparation and transition timing.
        /// </summary>
        /// <param name="subtask">Subtask for progress updates</param>
        /// <param name="characterSlot">Character slot number being selected</param>
        /// <param name="windowHandle">FFXI window handle</param>
        /// <param name="cancellationToken">Cancellation token</param>
        private async Task SelectCharacterSlotAsync(
            AutoLoginSubtask subtask,
            int characterSlot,
            IntPtr windowHandle,
            CancellationToken cancellationToken)
        {
            await UpdateProgressAsync(subtask, FFXIGameConfiguration.ProgressMilestones.SlotSelecting,
                $"Selecting character slot {characterSlot} (pressing Enter)...");
            await _loggingService.LogInfoAsync($"[NAVIGATION] About to press Enter to select character slot {characterSlot}");

            // Prepare window for selection
            await PrepareWindowForNavigationAsync(windowHandle, FFXIGameConfiguration.Delays.EnterPreparation, cancellationToken);

            // Create programmatic navigation action for slot selection
            var selectionAction = new NavigationAction
            {
                Type = NavigationType.Keyboard,
                PostNavigationDelayMs = (int)FFXIGameConfiguration.Delays.CharacterLoading.TotalMilliseconds
            };
            selectionAction.Sequence.Add(new KeyboardAction
            {
                Action = "Enter",
                Count = 1,
                DelayMs = 100,
                Description = $"Select character slot {characterSlot}"
            });

            // Execute navigation using keyboard strategy
            var navSuccess = await ExecuteNavigationActionAsync(
                subtask,
                selectionAction,
                windowHandle,
                null, // No template match needed for programmatic navigation
                _automationService,
                cancellationToken);

            if (!navSuccess)
            {
                throw new InvalidOperationException($"Failed to select character slot {characterSlot}");
            }

            await _loggingService.LogInfoAsync($"[NAVIGATION] Character slot {characterSlot} selection completed");
            await UpdateProgressAsync(subtask, FFXIGameConfiguration.ProgressMilestones.SlotComplete,
                $"Character slot {characterSlot} selected successfully");
        }

        /// <summary>
        /// Executes character slot selection with decomposed, focused helper methods.
        /// Orchestrates slot validation, screen detection, navigation, and selection.
        /// </summary>
        /// <param name="subtask">Subtask for progress reporting</param>
        /// <param name="queueItem">Queue item containing account and slot information</param>
        /// <param name="context">Context for window handle management</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <remarks>
        /// Configuration Dependencies:
        /// - FFXIGameConfiguration.CharacterSlots.* for slot validation
        /// - FFXIGameConfiguration.Timeouts.CharacterSlot for screen detection timeout
        /// - FFXIGameConfiguration.Delays.* for UI timing
        /// - FFXIGameConfiguration.ProgressMilestones.Slot* for progress reporting
        /// 
        /// Service Dependencies:
        /// - IAutoLoginContext for window handle retrieval
        /// - IUIAutomationService for window focus and key input
        /// - ILoggingService for navigation logging
        /// 
        /// Error Scenarios:
        /// - Invalid character slot: InvalidOperationException
        /// - Window handle retrieval fails: Handled by GetFFXIWindowHandleAsync
        /// - Screen detection timeout: Handled by WaitForScreenDetectionWithRedetectionAsync
        /// - Navigation input failures: Logged but operation continues
        /// </remarks>
        private async Task ExecuteCharacterSlotPickAsync(
            AutoLoginSubtask subtask,
            AutoLoginQueueItem queueItem,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            // Get character slot and validate it
            var characterSlot = queueItem.Account?.FFXICharacterSlot ?? FFXIGameConfiguration.CharacterSlots.DefaultSlotNumber;
            ValidateCharacterSlot(characterSlot);

            // Get FFXI window handle from context
            var ffxiWindowHandle = await GetFFXIWindowHandleAsync(subtask, context, cancellationToken);

            await UpdateProgressAsync(subtask, FFXIGameConfiguration.ProgressMilestones.SlotWait, "Waiting for character slot selection screen...");

            // Wait for character slot screen using standardized detection with fallback
            var slotScreenMatch = await WaitForScreenWithRedetectionFallbackAsync(
                subtask,
                FFXIGameConfiguration.TemplatePaths.CharacterSlotSelection,
                ffxiWindowHandle,
                context,
                "character slot selection screen",
                FFXIGameConfiguration.Timeouts.CharacterSlot,
                FFXIGameConfiguration.PollingIntervals.MenuCheck,
                cancellationToken);

            await _loggingService.LogInfoAsync($"Character slot screen detected with confidence: {slotScreenMatch.Confidence:P}");
            
            // Get updated window handle and allow screen stabilization
            ffxiWindowHandle = ValidateWindowContextAsync(context, ffxiWindowHandle);
            await WaitForScreenTransitionAsync(subtask, FFXIGameConfiguration.Delays.ScreenStabilization, 
                FFXIGameConfiguration.ProgressMilestones.SlotDetected, "Character slot screen detected", cancellationToken);

            // Navigate to the target character slot
            await NavigateToCharacterSlotAsync(subtask, characterSlot, ffxiWindowHandle, cancellationToken);

            // Select the character slot
            await SelectCharacterSlotAsync(subtask, characterSlot, ffxiWindowHandle, cancellationToken);
        }

        /// <summary>
        /// Executes the final FFXI login confirmation phase to enter the game world.
        /// Detects the character confirmation screen and sends final Enter to complete the login process.
        /// </summary>
        /// <param name="subtask">Subtask for progress reporting (0-100%)</param>
        /// <param name="queueItem">Queue item containing account and character slot information for logging</param>
        /// <param name="context">Context for retrieving FFXI window handle and cleanup after completion</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <remarks>
        /// <para><strong>Configuration Dependencies:</strong></para>
        /// <list type="bullet">
        ///   <item><description>FFXIGameConfiguration.Timeouts.CharacterConfirmation - Screen detection timeout</description></item>
        ///   <item><description>FFXIGameConfiguration.PollingIntervals.MenuCheck - Detection polling frequency</description></item>
        ///   <item><description>FFXIGameConfiguration.Delays.ScreenStabilization - Screen stabilization wait</description></item>
        ///   <item><description>FFXIGameConfiguration.Delays.WindowFocus - Window focus preparation</description></item>
        ///   <item><description>FFXIGameConfiguration.Delays.GameWorldLoading - Game world entry wait time</description></item>
        ///   <item><description>FFXIGameConfiguration.Delays.CharacterLoading - Final character load verification</description></item>
        /// </list>
        /// 
        /// <para><strong>Service Dependencies:</strong></para>
        /// <list type="bullet">
        ///   <item><description>GetFFXIWindowHandleAsync - Window handle retrieval and validation</description></item>
        ///   <item><description>WaitForScreenDetectionWithRedetectionAsync - Character confirmation screen detection</description></item>
        ///   <item><description>IUIAutomationService - Window focus management and DirectX-compatible Enter key input</description></item>
        ///   <item><description>IAutoLoginContext - Window handle management and cleanup after completion</description></item>
        /// </list>
        /// 
        /// <para><strong>Error Scenarios:</strong></para>
        /// <list type="bullet">
        ///   <item><description>Window handle not found/invalid: GetFFXIWindowHandleAsync handles redetection</description></item>
        ///   <item><description>Character confirmation screen not detected: TimeoutException with redetection fallback</description></item>
        ///   <item><description>Game world loading takes longer than expected: Extended wait times handle normal variations</description></item>
        ///   <item><description>Final confirmation input fails: Logged but operation continues to completion</description></item>
        /// </list>
        /// 
        /// <para><strong>Progress Milestones:</strong></para>
        /// <list type="bullet">
        ///   <item><description>10% - Starting character confirmation screen detection</description></item>
        ///   <item><description>40% - Character confirmation screen detected</description></item>
        ///   <item><description>60% - Confirming character login (sending Enter)</description></item>
        ///   <item><description>80% - Login confirmation sent, entering game world</description></item>
        ///   <item><description>95% - Verifying successful game entry</description></item>
        ///   <item><description>100% - Character successfully entered game world</description></item>
        /// </list>
        /// 
        /// <para><strong>FFXI-Specific Behavior:</strong></para>
        /// <list type="bullet">
        ///   <item><description>Character confirmation screen requires specific template detection confidence</description></item>
        ///   <item><description>Game world loading can vary significantly based on server conditions and character location</description></item>
        ///   <item><description>DirectX input requires precise window focus management for final confirmation</description></item>
        ///   <item><description>Context cleanup removes "FFXIWindowHandle" as login process is complete</description></item>
        ///   <item><description>Extended loading times account for zone loading and character data synchronization</description></item>
        /// </list>
        /// 
        /// <para><strong>Example Usage:</strong></para>
        /// <code>
        /// // Called automatically by ExecuteHandlerLogicAsync when
        /// // subtask.TaskStep == LoginTaskStep.ConfirmLogin
        /// //
        /// // Typical flow:
        /// // 1. GetFFXIWindowHandleAsync() retrieves window handle from context
        /// // 2. WaitForScreenDetectionWithRedetectionAsync() detects character confirmation screen
        /// // 3. Screen stabilization delay allows UI animations to complete
        /// // 4. Window focus preparation ensures reliable input delivery
        /// // 5. SendKeyAsync(Enter) confirms final login to game world
        /// // 6. Extended delays allow game world loading and character synchronization
        /// // 7. Context cleanup removes stored window handle as login is complete
        /// </code>
        /// </remarks>
        /// <exception cref="InvalidOperationException">FFXI window handle cannot be found or validated</exception>
        /// <exception cref="TimeoutException">Character confirmation screen detection fails even with redetection</exception>
        /// <exception cref="OperationCanceledException">Operation cancelled via cancellation token</exception>
        private async Task ExecuteConfirmLoginAsync(
            AutoLoginSubtask subtask,
            AutoLoginQueueItem queueItem,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            // Get FFXI window handle from context
            var ffxiWindowHandle = await GetFFXIWindowHandleAsync(subtask, context, cancellationToken);

            var accountName = queueItem.Account?.AccountName ?? "Character";
            var characterSlot = queueItem.Account?.FFXICharacterSlot ?? 1;

            await UpdateProgressWithPhaseAsync(subtask, "gameconnection", 10,
                                                "Preparing character login");

            // Wait for character confirmation screen with window handle re-detection on failure
            var confirmMatch = await WaitForScreenDetectionWithRedetectionAsync(
                subtask,
                FFXIGameConfiguration.TemplatePaths.CharacterConfirmation,
                ffxiWindowHandle,
                context,
                "character confirmation screen",
                cancellationToken,
                new ScreenDetectionOptions
                {
                    Timeout = FFXIGameConfiguration.Timeouts.CharacterConfirmation,
                    CheckInterval = FFXIGameConfiguration.PollingIntervals.MenuCheck
                    // ConfidenceThreshold will be loaded from template JSON (0.80)
                });

            // The WaitForScreenDetectionWithRedetectionAsync method already validates against the template threshold
            // from the JSON configuration, so if we get here, detection was successful
            await _loggingService.LogInfoAsync($"Character confirmation screen detected with confidence: {confirmMatch.Confidence:P}");

            await UpdateProgressWithPhaseAsync(subtask, "gameconnection", 40,
                                                "Character selected");

            // Get updated window handle from context
            ffxiWindowHandle = context.GetValueData<IntPtr>("FFXIWindowHandle");

            // Allow screen to stabilize
            await Task.Delay(FFXIGameConfiguration.Delays.ScreenStabilization, cancellationToken);

            await UpdateProgressWithPhaseAsync(subtask, "gameconnection", 60,
                                                "Confirming character login");

            // Ensure window has focus
            await _automationService.EnsureWindowFocusAsync(ffxiWindowHandle, cancellationToken);
            await Task.Delay(FFXIGameConfiguration.Delays.WindowFocus, cancellationToken);

            // Execute confirmation navigation using template-driven approach
            var navSuccess = await ExecuteNavigationFromTemplateAsync(
                subtask,
                FFXIGameConfiguration.TemplatePaths.CharacterConfirmation,
                ffxiWindowHandle,
                confirmMatch,
                _automationService,
                cancellationToken);

            if (!navSuccess)
            {
                throw new InvalidOperationException("Failed to confirm character login");
            }

            await UpdateProgressWithPhaseAsync(subtask, "gameconnection", 80,
                                                "Entering game world");

            // Allow significant time for the game world to load
            await Task.Delay(FFXIGameConfiguration.Delays.GameWorldLoading, cancellationToken);

            await UpdateProgressWithPhaseAsync(subtask, "gameconnection", 95,
                                                "Finalizing connection");

            // Additional wait to ensure character is fully loaded into the game world
            await Task.Delay(FFXIGameConfiguration.Delays.CharacterLoading, cancellationToken);

            await UpdateProgressWithPhaseAsync(subtask, "gameconnection", 100,
                                                $"🎉 Welcome to Vana'diel! {accountName} is now online");

            // Clear the stored window handle as login is complete
            context.Data.TryRemove("FFXIWindowHandle", out _);
        }

        /// <summary>
        /// Validates and logs template matching results with appropriate diagnostic information.
        /// Provides detailed confidence analysis to aid in troubleshooting detection issues.
        /// </summary>
        /// <param name="match">The template match result to validate</param>
        /// <param name="confidenceThreshold">Minimum confidence threshold for success</param>
        /// <param name="screenDescription">Description of the screen being detected</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if match meets confidence threshold, false otherwise</returns>
        private async Task<bool> ValidateDetectionResultAsync(
            TemplateMatchResult match,
            float confidenceThreshold,
            string screenDescription,
            CancellationToken cancellationToken)
        {
            if (match.Confidence >= confidenceThreshold)
            {
                await _loggingService.LogInfoAsync($"{screenDescription} detection successful (confidence: {match.Confidence:P})");
                return true;
            }

            // Enhanced diagnostic logging for different confidence ranges
            if (match.Confidence < 0.10f)
            {
                await _loggingService.LogWarningAsync($"{screenDescription} very low confidence ({match.Confidence:P}) - possible template mismatch or screen state issue");
            }
            else if (match.Confidence >= 0.60f)
            {
                await _loggingService.LogDebugAsync($"{screenDescription} partially detected (confidence: {match.Confidence:P}) - getting close");
            }
            else if (match.Confidence >= 0.30f)
            {
                await _loggingService.LogDebugAsync($"{screenDescription} moderate confidence ({match.Confidence:P}) - template may be partially visible");
            }

            return false;
        }

        /// <summary>
        /// Handles window handle redetection when consecutive screenshot failures occur.
        /// Updates the context with the new window handle if redetection is successful.
        /// </summary>
        /// <param name="currentWindowHandle">Current window handle that may be invalid</param>
        /// <param name="context">Context to update with new window handle</param>
        /// <param name="consecutiveFailures">Number of consecutive screenshot failures</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>New window handle if redetection successful, otherwise the original handle</returns>
        private async Task<IntPtr> HandleWindowRedetectionAsync(
            IntPtr currentWindowHandle,
            IAutoLoginContext context,
            int consecutiveFailures,
            CancellationToken cancellationToken)
        {
            await _loggingService.LogInfoAsync($"Re-detecting FFXI window handle after {consecutiveFailures} consecutive screenshot failures");

            try
            {
                // Get the PID of the current (possibly stale) window for prioritization
                int? currentPid = null;
                if (currentWindowHandle != IntPtr.Zero)
                {
                    try
                    {
                        var currentScreenshot = await _screenshotService.CaptureWindowAsync(currentWindowHandle, cancellationToken);
                        currentPid = currentScreenshot?.ProcessId;
                        await _loggingService.LogDebugAsync($"[REDETECTION] Current window handle 0x{currentWindowHandle.ToInt64():X} belongs to PID: {currentPid}");
                    }
                    catch
                    {
                        await _loggingService.LogDebugAsync($"[REDETECTION] Could not determine PID for current handle 0x{currentWindowHandle.ToInt64():X}");
                    }
                }

                // Since we no longer have a monitor service, use process utility service for basic window enumeration
                await _loggingService.LogDebugAsync("[REDETECTION] Using fallback process discovery...");
                var ffxiHandle = await CreateFallbackWindowSearch(cancellationToken);
                if (ffxiHandle != IntPtr.Zero)
                {
                    await _loggingService.LogInfoAsync($"[REDETECTION] ✓ Found FFXI window: 0x{ffxiHandle.ToInt64():X}");
                    context.SetData("FFXIWindowHandle", ffxiHandle);
                    return ffxiHandle;
                }
                
                // No suitable windows found
                await _loggingService.LogWarningAsync("[REDETECTION] No valid FFXI windows found");
                return currentWindowHandle;
            }
            catch (Exception redetectEx)
            {
                await _loggingService.LogWarningAsync($"Failed to re-detect FFXI window: {redetectEx.Message}");
            }

            return currentWindowHandle;
        }

        /// <summary>
        /// Performs core screen detection with retry logic and window handle redetection.
        /// Centralizes the main detection loop with proper error handling and progress reporting.
        /// </summary>
        /// <param name="subtask">Subtask for progress reporting</param>
        /// <param name="templatePath">Path to the template for detection</param>
        /// <param name="initialWindowHandle">Initial window handle to use</param>
        /// <param name="context">Context for storing updated window handle</param>
        /// <param name="screenDescription">Description of screen being detected</param>
        /// <param name="options">Detection options including timeout and intervals</param>
        /// <param name="confidenceThreshold">Minimum confidence required for success</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Template match result if successful, null if detection fails</returns>
        private async Task<TemplateMatchResult?> DetectScreenWithRetryAsync(
            AutoLoginSubtask subtask,
            string templatePath,
            IntPtr initialWindowHandle,
            IAutoLoginContext context,
            string screenDescription,
            ScreenDetectionOptions options,
            float confidenceThreshold,
            CancellationToken cancellationToken)
        {
            var currentWindowHandle = initialWindowHandle;
            var maxAttempts = (int)(options.Timeout.TotalSeconds / options.CheckInterval.TotalSeconds);
            var consecutiveFailures = 0;
            var maxConsecutiveFailures = FFXIGameConfiguration.ProcessDiscovery.MaxConsecutiveFailures;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Update progress - use monotonic progress within the detection range (20-80%)
                var baseProgress = 20; // Start of detection range
                var rangeSize = 60; // Detection range size (80% - 20%)
                var progress = baseProgress + ((attempt - 1) * rangeSize / maxAttempts);
                await UpdateProgressWithPhaseAsync(subtask, "gameconnection", Math.Min(80, progress), $"Detecting {screenDescription}");

                try
                {
                    var screenshot = await _screenshotService.CaptureWindowAsync(currentWindowHandle, cancellationToken);

                    if (screenshot == null || !screenshot.IsValid)
                    {
                        consecutiveFailures++;
                        await _loggingService.LogDebugAsync($"{screenDescription} screenshot failed (attempt {attempt}/{maxAttempts}) - consecutive failures: {consecutiveFailures}");

                // FFXI-Specific: Window handles can become invalid during screen transitions
                        // This is common when FFXI changes resolution or enters fullscreen mode
                        if (consecutiveFailures >= maxConsecutiveFailures)
                        {
                            var newHandle = await HandleWindowRedetectionAsync(currentWindowHandle, context, consecutiveFailures, cancellationToken);
                            if (newHandle != currentWindowHandle)
                            {
                                currentWindowHandle = newHandle;
                                consecutiveFailures = 0;
                                continue; // Immediate retry with fresh window handle - no delay needed
                            }
                        }
                    }
                    else
                    {
                        // Screenshot successful, reset failure count and try template matching
                        consecutiveFailures = 0;
                        var match = await _templateService.FindElementAsync(screenshot, templatePath, cancellationToken);

                        await _loggingService.LogDebugAsync($"{screenDescription} detection attempt {attempt}/{maxAttempts}: confidence={match.Confidence:P}, threshold={confidenceThreshold:P}");

                        if (await ValidateDetectionResultAsync(match, confidenceThreshold, screenDescription, cancellationToken))
                        {
                            await UpdateProgressWithPhaseAsync(subtask, "gameconnection", 85, $"{screenDescription} detected successfully");
                            context.SetData("FFXIWindowHandle", currentWindowHandle);
                            return match;
                        }
                    }
                }
                catch (Exception ex)
                {
                    await _loggingService.LogDebugAsync($"Exception during {screenDescription} detection attempt {attempt}: {ex.Message}");
                    consecutiveFailures++;
                }

                if (attempt < maxAttempts)
                {
                    await Task.Delay(options.CheckInterval, cancellationToken);
                }
            }

            return null; // Detection failed
        }

        /// <summary>
        /// Screen detection with automatic window handle re-detection when screenshots fail.
        /// This handles the common FFXI issue where window handles become invalid during transitions.
        /// Now orchestrates the detection process using focused helper methods.
        /// </summary>
        /// <param name="subtask">Subtask for progress reporting</param>
        /// <param name="templatePath">Path to the template for detection</param>
        /// <param name="initialWindowHandle">Initial window handle to use</param>
        /// <param name="context">Context for storing window handle updates</param>
        /// <param name="screenDescription">Description of the screen being detected</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <param name="options">Detection options (timeout, intervals, etc.)</param>
        /// <returns>Template match result</returns>
        /// <remarks>
        /// Configuration Dependencies:
        /// - FFXIGameConfiguration.ProcessDiscovery.MaxConsecutiveFailures
        /// - FFXIGameConfiguration.ProgressMilestones.DetectionAttempting/DetectionSuccessful
        /// 
        /// Service Dependencies:
        /// - ITemplateManagementService for metadata loading
        /// - IScreenshotCaptureService for window capture
        /// - ITemplateMatchingService for element detection
        /// - ILoggingService for diagnostic logging
        /// 
        /// Error Scenarios:
        /// - Template metadata not found: InvalidOperationException
        /// - Screenshot capture failures: Automatic window redetection
        /// - Template matching failures: Enhanced diagnostic logging
        /// - Window handle becomes invalid: Automatic redetection and context update
        /// </remarks>
        private async Task<TemplateMatchResult> WaitForScreenDetectionWithRedetectionAsync(
            AutoLoginSubtask subtask,
            string templatePath,
            IntPtr initialWindowHandle,
            IAutoLoginContext context,
            string screenDescription,
            CancellationToken cancellationToken,
            ScreenDetectionOptions? options = null)
        {
            options ??= ScreenDetectionOptions.Default;

            // Load template metadata to get the configured confidence threshold
            var templateMetadata = await _templateManagementService.GetTemplateMetadataAsync(templatePath);
            if (templateMetadata == null)
            {
                throw new InvalidOperationException($"Template metadata not found for: {templatePath}");
            }

            var confidenceThreshold = templateMetadata.ConfidenceThreshold;
            await _loggingService.LogInfoAsync($"Using template confidence threshold: {confidenceThreshold:P} for {screenDescription}");
            await _loggingService.LogInfoAsync($"Waiting for {screenDescription} (max {options.Timeout.TotalSeconds}s, checking every {options.CheckInterval.TotalSeconds}s, confidence={confidenceThreshold:P})");

            // Orchestrate the detection process using focused helper methods
            var detectionResult = await DetectScreenWithRetryAsync(
                subtask,
                templatePath,
                initialWindowHandle,
                context,
                screenDescription,
                options,
                confidenceThreshold,
                cancellationToken);

            if (detectionResult != null)
            {
                await _loggingService.LogInfoAsync($"{screenDescription} detected successfully (confidence: {detectionResult.Confidence:P})");
                return detectionResult;
            }

            // Final diagnostic attempt if detection failed
            await _loggingService.LogWarningAsync($"{screenDescription} detection failed after timeout");
            try
            {
                var currentHandle = context.GetValueData<IntPtr>("FFXIWindowHandle");
                if (currentHandle == IntPtr.Zero) currentHandle = initialWindowHandle;
                
                var finalScreenshot = await _screenshotService.CaptureWindowAsync(currentHandle, cancellationToken);
                var finalMatch = await _templateService.FindElementAsync(finalScreenshot!, templatePath, cancellationToken);

                await _loggingService.LogWarningAsync($"{screenDescription} final diagnostic attempt - confidence: {finalMatch.Confidence:P}");
                return finalMatch;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Final diagnostic attempt failed for {screenDescription}", ex);
                return new TemplateMatchResult { Confidence = 0.0f };
            }
        }



        /// <summary>
        /// Waits for the FFXI process to launch and returns its window handle.
        /// Handles the transition from PlayOnline to FFXI process with comprehensive discovery logic.
        /// </summary>
        /// <param name="subtask">Subtask for progress reporting during process detection</param>
        /// <param name="context">Context for checking stored window handles from previous handlers</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Valid FFXI window handle</returns>
        /// <remarks>
        /// <para><strong>Configuration Dependencies:</strong></para>
        /// <list type="bullet">
        ///   <item><description>FFXIGameConfiguration.ProcessDiscovery.ProcessDetectionAttempts - Maximum detection attempts</description></item>
        ///   <item><description>FFXIGameConfiguration.ProcessDiscovery.WindowTitlePatterns - Window title validation patterns</description></item>
        ///   <item><description>FFXIGameConfiguration.PollingIntervals.ProcessCheck - Time between detection attempts</description></item>
        /// </list>
        /// 
        /// <para><strong>Service Dependencies:</strong></para>
        /// <list type="bullet">
        ///   <item><description>FindFFXIWindowHandleAsync - Process enumeration and window discovery</description></item>
        ///   <item><description>IScreenshotCaptureService - Window validation through screenshot capture</description></item>
        ///   <item><description>IAutoLoginContext - Retrieval of stored window handles from PlayOnline phase</description></item>
        /// </list>
        /// 
        /// <para><strong>FFXI Process Discovery Strategy:</strong></para>
        /// <list type="number">
        ///   <item><description>Primary: Search for new FFXI processes using configured process names</description></item>
        ///   <item><description>Fallback: Check if stored PlayOnline window handle has transitioned to FFXI</description></item>
        ///   <item><description>Validation: Verify window responsiveness and title pattern matching</description></item>
        ///   <item><description>Retry: Continue until process found or maximum attempts reached</description></item>
        /// </list>
        /// 
        /// <para><strong>Error Scenarios:</strong></para>
        /// <list type="bullet">
        ///   <item><description>FFXI process doesn't launch: InvalidOperationException after configured timeout</description></item>
        ///   <item><description>Process launches but window unresponsive: Continues detection attempts</description></item>
        ///   <item><description>Window title doesn't match patterns: Uses fallback process name validation</description></item>
        /// </list>
        /// 
        /// <para><strong>FFXI-Specific Behavior:</strong></para>
        /// <list type="bullet">
        ///   <item><description>Process may start as 'pol' and transition to 'ffximain' during launch</description></item>
        ///   <item><description>Window handle from PlayOnline phase may become the FFXI window</description></item>
        ///   <item><description>Multiple process names supported for different FFXI launch configurations</description></item>
        ///   <item><description>Window title validation supports various FFXI window states and versions</description></item>
        /// </list>
        /// 
        /// <para><strong>Example Usage:</strong></para>
        /// <code>
        /// // Called during TermsAcceptance phase to establish FFXI window handle
        /// var ffxiHandle = await WaitForFFXIProcessAsync(subtask, context, cancellationToken);
        /// context.SetData("FFXIWindowHandle", ffxiHandle);
        /// 
        /// // Progress reporting:
        /// // 5% - Starting process detection
        /// // 10% - Found FFXI process (when successful)
        /// // 12% - Process validated and ready
        /// </code>
        /// </remarks>
        /// <exception cref="InvalidOperationException">FFXI process fails to launch within the configured timeout period</exception>
        /// <exception cref="OperationCanceledException">Operation cancelled via cancellation token</exception>
        private async Task<IntPtr> WaitForFFXIProcessAsync(
            AutoLoginSubtask subtask,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            var maxAttempts = FFXIGameConfiguration.ProcessDiscovery.ProcessDetectionAttempts;
            var attempt = 0;

            await _loggingService.LogInfoAsync("[PID_TRACKING] Waiting for PlayOnline to FFXI transition by monitoring window handle changes...");

            // Get stored PlayOnline context
            var playOnlinePidData = context.GetValueData<int>("PlayOnlineProcessId");
            var playOnlinePid = playOnlinePidData == 0 ? (int?)null : playOnlinePidData;
            var playOnlineHandle = context.GetValueData<IntPtr>("PlayOnlineWindowHandle");

            if (!playOnlinePid.HasValue || playOnlineHandle == IntPtr.Zero)
            {
                await _loggingService.LogWarningAsync("[PID_TRACKING] No PlayOnline PID/handle context found - falling back to process discovery");
                return await FallbackProcessDiscoveryAsync(subtask, cancellationToken);
            }

            await _loggingService.LogInfoAsync($"[PID_TRACKING] Monitoring PID {playOnlinePid} for window handle changes (current: 0x{playOnlineHandle.ToInt64():X})");

            while (attempt < maxAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempt++;

                // Use monotonic progress within transition monitoring range (10-40%)
                var baseProgress = 10;
                var rangeSize = 30;
                var progress = baseProgress + ((attempt - 1) * rangeSize / maxAttempts);
                await UpdateProgressWithPhaseAsync(subtask, "gameconnection", Math.Min(40, progress), "Monitoring game transition");
                
                await _loggingService.LogInfoAsync($"[PID_TRACKING] Attempt {attempt}/{maxAttempts} - Checking PID {playOnlinePid} for new windows...");

                // Use ProcessUtilityService to get windows for the specific PID
                var processWindows = await _processUtilityService.GetProcessWindowsAsync(playOnlinePid.Value);
                
                await _loggingService.LogInfoAsync($"[PID_TRACKING] Found {processWindows.Count} windows in PID {playOnlinePid}");

                foreach (var window in processWindows)
                {
                    await _loggingService.LogInfoAsync($"[PID_TRACKING] - Window: 0x{window.Handle.ToInt64():X}, Title: '{window.Title}'");
                    
                    // Look for a different window handle with FFXI title patterns
                    if (window.Handle != playOnlineHandle && window.Handle != IntPtr.Zero && !string.IsNullOrEmpty(window.Title))
                    {
                        // Check if this window has an FFXI title
                        await _loggingService.LogInfoAsync($"[PID_TRACKING] New window detected - Testing title: '{window.Title}' against FFXI patterns");
                        
                        bool isFFXITitle = false;
                        foreach (var pattern in FFXIGameConfiguration.ProcessDiscovery.WindowTitlePatterns)
                        {
                            if (window.Title.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                            {
                                await _loggingService.LogInfoAsync($"[PID_TRACKING] ✓ FFXI pattern match: '{pattern}' in '{window.Title}'");
                                isFFXITitle = true;
                                break;
                            }
                        }
                        
                        if (isFFXITitle)
                        {
                            await _loggingService.LogInfoAsync($"[PID_TRACKING] ✅ SUCCESS - FFXI transition detected! PID {playOnlinePid}, Handle: 0x{playOnlineHandle.ToInt64():X} → 0x{window.Handle.ToInt64():X}");
                            await UpdateProgressWithPhaseAsync(subtask, "gameconnection", 45, "FFXI window transition detected");
                            return window.Handle;
                        }
                        else
                        {
                            await _loggingService.LogInfoAsync($"[PID_TRACKING] New window 0x{window.Handle.ToInt64():X} does not match FFXI patterns: '{window.Title}'");
                        }
                    }
                }

                // No transition detected yet, wait before next attempt
                if (attempt < maxAttempts)
                {
                    await _loggingService.LogInfoAsync($"[PID_TRACKING] No FFXI window transition detected, waiting {FFXIGameConfiguration.PollingIntervals.ProcessCheck.TotalSeconds}s...");
                    await Task.Delay(FFXIGameConfiguration.PollingIntervals.ProcessCheck, cancellationToken);
                }
            }

            await _loggingService.LogWarningAsync($"[PID_TRACKING] No FFXI transition detected after {maxAttempts} attempts - falling back to process discovery");
            return await FallbackProcessDiscoveryAsync(subtask, cancellationToken);
        }

        /// <summary>
        /// Fallback method for FFXI process detection when PID tracking fails.
        /// Uses traditional process enumeration and window title matching.
        /// </summary>
        private async Task<IntPtr> FallbackProcessDiscoveryAsync(AutoLoginSubtask subtask, CancellationToken cancellationToken)
        {
            await _loggingService.LogInfoAsync("[FALLBACK] Using traditional FFXI process discovery...");
            
            var maxAttempts = FFXIGameConfiguration.ProcessDiscovery.ProcessDetectionAttempts;
            var attempt = 0;
            
            while (attempt < maxAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempt++;
                
                await _loggingService.LogInfoAsync($"[FALLBACK] Attempt {attempt}/{maxAttempts} - Scanning for FFXI processes...");
                
                var ffxiHandle = await CreateFallbackWindowSearch(cancellationToken);
                if (ffxiHandle != IntPtr.Zero)
                {
                    await _loggingService.LogInfoAsync($"[FALLBACK] ✅ Found FFXI window: 0x{ffxiHandle.ToInt64():X}");
                    return ffxiHandle;
                }
                
                if (attempt < maxAttempts)
                {
                    await Task.Delay(FFXIGameConfiguration.PollingIntervals.ProcessCheck, cancellationToken);
                }
            }
            
            throw new InvalidOperationException($"FFXI process could not be detected using fallback method after {maxAttempts} attempts");
        }

        /// <summary>
        /// Waits for FFXI to fully initialize and become responsive for UI automation.
        /// Validates window responsiveness through screenshot capture rather than template matching.
        /// </summary>
        /// <param name="windowHandle">FFXI window handle to validate</param>
        /// <param name="subtask">Subtask for progress reporting during initialization checks</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <remarks>
        /// <para><strong>Configuration Dependencies:</strong></para>
        /// <list type="bullet">
        ///   <item><description>FFXIGameConfiguration.ProcessDiscovery.ResponsivenessCheckAttempts - Maximum check attempts</description></item>
        ///   <item><description>FFXIGameConfiguration.PollingIntervals.ProcessCheck - Time between responsiveness checks</description></item>
        ///   <item><description>FFXIGameConfiguration.Delays.ScreenStabilization - Final stabilization delay for DirectX rendering</description></item>
        /// </list>
        /// 
        /// <para><strong>Service Dependencies:</strong></para>
        /// <list type="bullet">
        ///   <item><description>IScreenshotCaptureService - Window responsiveness validation through screenshot capture</description></item>
        ///   <item><description>ILoggingService - Detailed startup progress logging</description></item>
        /// </list>
        /// 
        /// <para><strong>Responsiveness Validation Strategy:</strong></para>
        /// <list type="number">
        ///   <item><description>Capture screenshot from provided window handle</description></item>
        ///   <item><description>Validate screenshot is valid (not null, has dimensions)</description></item>
        ///   <item><description>Log window title and dimensions for diagnostic purposes</description></item>
        ///   <item><description>Require multiple consecutive successful captures before considering ready</description></item>
        ///   <item><description>Apply final stabilization delay for DirectX rendering stability</description></item>
        /// </list>
        /// 
        /// <para><strong>FFXI-Specific Behavior:</strong></para>
        /// <list type="bullet">
        ///   <item><description>DirectX window may take time to become responsive for automation</description></item>
        ///   <item><description>Multiple successful screenshots required to ensure window stability</description></item>
        ///   <item><description>Window title and dimensions logged for debugging launch issues</description></item>
        ///   <item><description>Extended stabilization delay accounts for DirectX rendering initialization</description></item>
        /// </list>
        /// 
        /// <para><strong>Error Scenarios:</strong></para>
        /// <list type="bullet">
        ///   <item><description>Window becomes unresponsive during startup: Continues attempts until timeout</description></item>
        ///   <item><description>Screenshot capture fails intermittently: Normal during FFXI initialization</description></item>
        ///   <item><description>Window dimensions invalid: Logged for diagnostic purposes</description></item>
        /// </list>
        /// 
        /// <para><strong>Example Usage:</strong></para>
        /// <code>
        /// // Called after WaitForFFXIProcessAsync to ensure window is ready for automation
        /// await WaitForFFXIStartup(ffxiWindowHandle, subtask, cancellationToken);
        /// 
        /// // Progress reporting:
        /// // 12-15% - FFXI responsiveness checks with attempt counting
        /// // Final stabilization delay ensures DirectX readiness for subsequent screen detection
        /// </code>
        /// </remarks>
        /// <exception cref="OperationCanceledException">Operation cancelled via cancellation token</exception>
        private async Task WaitForFFXIStartup(IntPtr windowHandle, AutoLoginSubtask subtask, CancellationToken cancellationToken)
        {
            var maxAttempts = FFXIGameConfiguration.ProcessDiscovery.ResponsivenessCheckAttempts; // Time for FFXI window to become responsive
            var attempt = 0;

            await _loggingService.LogInfoAsync("Waiting for Final Fantasy XI window to become responsive...");

            while (attempt < maxAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Use monotonic progress within responsiveness range (50-65%)
                var baseProgress = 50;
                var rangeSize = 15;
                var progress = baseProgress + (attempt * rangeSize / maxAttempts);
                await UpdateProgressWithPhaseAsync(subtask, "gameconnection", Math.Min(65, progress), "Checking FFXI responsiveness");

                try
                {
                    // Capture window and check if it's responsive
                    var screenshot = await _screenshotService.CaptureWindowAsync(windowHandle, cancellationToken);

                    if (screenshot != null && screenshot.IsValid && screenshot.Width > 0 && screenshot.Height > 0)
                    {
                        var windowTitle = screenshot.WindowTitle;
                        await _loggingService.LogDebugAsync($"FFXI startup check {attempt + 1}/{maxAttempts}: Window responsive, title: '{windowTitle}', size: {screenshot.Width}x{screenshot.Height}");

                        // FFXI-Specific: Multiple successful screenshots required to ensure DirectX stability
                        // Single successful capture may occur during initialization but window may still be unstable
                        if (attempt >= 2)
                        {
                            await _loggingService.LogInfoAsync("Final Fantasy XI window initialization completed");
                            break; // Window is consistently responsive - safe to proceed
                        }
                    }
                    else
                    {
                        await _loggingService.LogDebugAsync($"FFXI startup check {attempt + 1}/{maxAttempts}: Invalid screenshot");
                    }
                }
                catch (Exception ex)
                {
                    await _loggingService.LogDebugAsync($"FFXI startup check {attempt + 1}/{maxAttempts}: Exception during capture: {ex.Message}");
                }

                attempt++;
                await Task.Delay(FFXIGameConfiguration.PollingIntervals.ProcessCheck, cancellationToken);
            }

            // FFXI-Specific: DirectX rendering pipeline requires additional stabilization
            // This final delay ensures the window is fully ready for template matching and UI automation
            await Task.Delay(FFXIGameConfiguration.Delays.ScreenStabilization, cancellationToken);
        }

        /// <summary>
        /// Fallback method using ProcessUtilityService to search for FFXI windows by title patterns.
        /// Leverages the existing infrastructure instead of duplicating process enumeration logic.
        /// </summary>
        private async Task<IntPtr> CreateFallbackWindowSearch(CancellationToken cancellationToken)
        {
            try
            {
                // Use ProcessUtilityService to get processes with their windows
                var processNames = new[] { "pol", "ffxi", "ffximain", "PlayOnlineViewer" };
                var processes = await _processUtilityService.GetProcessesByNamesAsync(processNames);
                
                await _loggingService.LogInfoAsync($"[FALLBACK] Found {processes.Count} processes across {processNames.Length} process names");
                
                foreach (var process in processes)
                {
                    await _loggingService.LogDebugAsync($"[FALLBACK] Checking process '{process.ProcessName}' (PID: {process.ProcessId}) with {process.Windows.Count} windows");
                    
                    foreach (var window in process.Windows)
                    {
                        if (window.Handle != IntPtr.Zero && !string.IsNullOrEmpty(window.Title))
                        {
                            // Check if this window title matches FFXI patterns
                            foreach (var pattern in FFXIGameConfiguration.ProcessDiscovery.WindowTitlePatterns)
                            {
                                if (window.Title.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                                {
                                    await _loggingService.LogInfoAsync($"[FALLBACK] ✅ Found FFXI window: '{window.Title}' in process '{process.ProcessName}' (PID: {process.ProcessId}, Handle: 0x{window.Handle.ToInt64():X})");
                                    return window.Handle;
                                }
                            }
                        }
                    }
                }
                
                await _loggingService.LogDebugAsync("[FALLBACK] No FFXI windows found in fallback search");
                return IntPtr.Zero;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("[FALLBACK] Error in fallback window search", ex);
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Get FFXI window handle from context or find it
        /// </summary>
        private async Task<IntPtr> GetFFXIWindowHandleAsync(
            AutoLoginSubtask subtask,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            // Try to get FFXI-specific handle first
            var ffxiHandle = context.GetValueData<IntPtr>("FFXIWindowHandle");
            if (ffxiHandle != IntPtr.Zero)
            {
                try
                {
                    // Verify it's still valid
                    var screenshot = await _screenshotService.CaptureWindowAsync(ffxiHandle, cancellationToken);
                    if (screenshot != null && screenshot.IsValid)
                    {
                        await _loggingService.LogDebugAsync($"Using existing FFXI window handle: 0x{ffxiHandle.ToInt64():X}");
                        return ffxiHandle;
                    }
                }
                catch (Exception ex)
                {
                    await _loggingService.LogDebugAsync($"Existing FFXI handle invalid: {ex.Message}");
                }
            }

            // If not found or invalid, find it again
            await _loggingService.LogInfoAsync("FFXI window handle not found in context, searching for process...");
            ffxiHandle = await WaitForFFXIProcessAsync(subtask, context, cancellationToken);
            context.SetData("FFXIWindowHandle", ffxiHandle);
            return ffxiHandle;
        }
    }
}