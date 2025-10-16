using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.Services.AutoLogin.ScreenDetection;
using FFXIManager.Services.AutoLogin.Configuration;
using FFXIManager.Services.AutoLogin.Navigation;
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
        private readonly IFFXIScreenDetectionService _screenDetectionService;
        private readonly IFFXIProcessDiscoveryService _processDiscoveryService;
        private readonly IWindowHandleManagementService _windowHandleService;
        private readonly ICharacterNavigationService _characterNavigationService;

        public FFXIGameHandler(
            ILoggingService loggingService,
            IScreenshotCaptureService screenshotService,
            ITemplateMatchingService templateService,
            ITemplateManagementService templateManagementService,
            IUIAutomationService automationService,
            IAutoLoginContextService contextService,
            IFFXIScreenDetectionService screenDetectionService,
            IFFXIProcessDiscoveryService processDiscoveryService,
            IWindowHandleManagementService windowHandleService,
            ICharacterNavigationService characterNavigationService)
            : base(loggingService, screenshotService, templateService, templateManagementService)
        {
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
            _contextService = contextService ?? throw new ArgumentNullException(nameof(contextService));
            _screenDetectionService = screenDetectionService ?? throw new ArgumentNullException(nameof(screenDetectionService));
            _processDiscoveryService = processDiscoveryService ?? throw new ArgumentNullException(nameof(processDiscoveryService));
            _windowHandleService = windowHandleService ?? throw new ArgumentNullException(nameof(windowHandleService));
            _characterNavigationService = characterNavigationService ?? throw new ArgumentNullException(nameof(characterNavigationService));
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
            var ffxiWindowHandle = await _processDiscoveryService.WaitForFFXIProcessAsync(subtask, context, cancellationToken);

            // Store FFXI window handle for subsequent steps - critical for cross-task communication
            // Other handlers (CharacterSelection, etc.) will retrieve this handle from context
            context.SetData("FFXIWindowHandle", ffxiWindowHandle);

            // FFXI-Specific: DirectX window requires time to become responsive for automation
            // Multiple screenshot attempts ensure window is stable before UI interaction
            await _processDiscoveryService.WaitForFFXIStartupAsync(ffxiWindowHandle, subtask, cancellationToken);

            await UpdateProgressWithPhaseAsync(subtask, "gameconnection", FFXIGameConfiguration.ProgressMilestones.TermsScreenDetection,
                                                "Loading game interface");

            // Wait for FFXI accept terms screen using standardized detection with fallback
            var termsMatch = await _screenDetectionService.WaitForScreenWithRedetectionFallbackAsync(
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
            ffxiWindowHandle = _windowHandleService.ValidateWindowContextAsync(context, "FFXIWindowHandle", ffxiWindowHandle);

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
            var ffxiWindowHandle = await _processDiscoveryService.GetFFXIWindowHandleAsync(subtask, context, cancellationToken);

            await UpdateProgressWithPhaseAsync(subtask, "gameconnection", FFXIGameConfiguration.ProgressMilestones.MenuWait,
                                                "Loading character menu");

            // Wait for main menu screen using standardized detection with fallback
            var mainMenuMatch = await _screenDetectionService.WaitForScreenWithRedetectionFallbackAsync(
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
            ffxiWindowHandle = _windowHandleService.ValidateWindowContextAsync(context, "FFXIWindowHandle", ffxiWindowHandle);

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
            _characterNavigationService.ValidateCharacterSlot(characterSlot);

            // Get FFXI window handle from context
            var ffxiWindowHandle = await _processDiscoveryService.GetFFXIWindowHandleAsync(subtask, context, cancellationToken);

            await UpdateProgressAsync(subtask, FFXIGameConfiguration.ProgressMilestones.SlotWait, "Waiting for character slot selection screen...");

            // Wait for character slot screen using standardized detection with fallback
            var slotScreenMatch = await _screenDetectionService.WaitForScreenWithRedetectionFallbackAsync(
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
            ffxiWindowHandle = _windowHandleService.ValidateWindowContextAsync(context, "FFXIWindowHandle", ffxiWindowHandle);
            await WaitForScreenTransitionAsync(subtask, FFXIGameConfiguration.Delays.ScreenStabilization,
                FFXIGameConfiguration.ProgressMilestones.SlotDetected, "Character slot screen detected", cancellationToken);

            // Navigate to the target character slot
            await _characterNavigationService.NavigateToCharacterSlotAsync(subtask, characterSlot, ffxiWindowHandle, cancellationToken);

            // Select the character slot
            await _characterNavigationService.SelectCharacterSlotAsync(subtask, characterSlot, ffxiWindowHandle, cancellationToken);
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
            var ffxiWindowHandle = await _processDiscoveryService.GetFFXIWindowHandleAsync(subtask, context, cancellationToken);

            var accountName = queueItem.Account?.AccountName ?? "Character";
            var characterSlot = queueItem.Account?.FFXICharacterSlot ?? 1;

            await UpdateProgressWithPhaseAsync(subtask, "gameconnection", 10,
                                                "Preparing character login");

            // Wait for character confirmation screen with window handle re-detection on failure
            var confirmMatch = await _screenDetectionService.WaitForScreenDetectionWithRedetectionAsync(
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

    }
}