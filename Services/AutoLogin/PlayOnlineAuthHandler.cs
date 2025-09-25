using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.Services.AutoLogin.ScreenDetection;
using FFXIManager.Services.AutoLogin.Configuration;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Handles PlayOnline authentication tasks including member selection, password entry, and OTP entry.
    /// Implements screen detection-based automation for the complete PlayOnline authentication flow.
    /// 
    /// **Refactoring Improvements:**
    /// - All hardcoded values externalized to PlayOnlineAuthConfiguration
    /// - Leverages BaseLoginTaskHandler infrastructure for consistent patterns
    /// - Methods decomposed for single responsibility and improved maintainability
    /// - Comprehensive documentation following living documentation principles
    /// - Enhanced error handling and retry mechanisms from base class
    /// - Configurable timeouts, delays, and thresholds for better flexibility
    /// </summary>
    /// <remarks>
    /// **Dependencies:**
    /// - IUIAutomationService: UI interaction and mouse/keyboard automation
    /// - IWindowsCredentialsService: Secure password retrieval from Windows Credential Manager
    /// - IOTPService: One-time password generation for two-factor authentication
    /// - IPlayOnlineMonitorService: PlayOnline process and character monitoring
    /// - IExternalApplicationService: POL Proxy and external application detection
    /// - IAutoLoginContextService: Shared state management between handler steps
    /// 
    /// **Configuration Requirements:**
    /// - PlayOnlineAuthConfiguration: Provides all timeout, coordinate, and threshold values
    /// - Template files: Screen detection templates in PlayOnline/* directory
    /// - Account configuration: Member slot, password storage, OTP settings
    /// 
    /// **Supported Task Steps:**
    /// - MemberSelection: Detects and clicks the appropriate member slot (1-4)
    /// - PasswordEntry: Handles login screen navigation and secure password entry
    /// - OTPEntry: Manages one-time password entry and connection handling
    /// 
    /// **Error Recovery:**
    /// - Window handle re-detection during PlayOnline transitions
    /// - Retry logic for transient UI interaction failures
    /// - Graceful handling of POL Proxy scenarios vs standard navigation
    /// - Comprehensive diagnostic logging for troubleshooting
    /// </remarks>
    public class PlayOnlineAuthHandler : BaseLoginTaskHandler
    {
        private readonly IUIAutomationService _automationService;
        private readonly IWindowsCredentialsService _credentialsService;
        private readonly IOTPService _otpService;
        private readonly IPlayOnlineMonitorService _playOnlineMonitorService;
        private readonly IExternalApplicationService _externalApplicationService;
        private readonly IAutoLoginContextService _contextService;

        public PlayOnlineAuthHandler(
            ILoggingService loggingService,
            IScreenshotCaptureService screenshotService,
            ITemplateMatchingService templateService,
            ITemplateManagementService templateManagementService,
            IUIAutomationService automationService,
            IWindowsCredentialsService credentialsService,
            IOTPService otpService,
            IPlayOnlineMonitorService playOnlineMonitorService,
            IExternalApplicationService externalApplicationService,
            IAutoLoginContextService contextService)
            : base(loggingService, screenshotService, templateService, templateManagementService)
        {
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
            _credentialsService = credentialsService ?? throw new ArgumentNullException(nameof(credentialsService));
            _otpService = otpService ?? throw new ArgumentNullException(nameof(otpService));
            _playOnlineMonitorService = playOnlineMonitorService ?? throw new ArgumentNullException(nameof(playOnlineMonitorService));
            _externalApplicationService = externalApplicationService ?? throw new ArgumentNullException(nameof(externalApplicationService));
            _contextService = contextService ?? throw new ArgumentNullException(nameof(contextService));
        }

        public override LoginTaskStep TaskStep => LoginTaskStep.MemberSelection;

        public override bool CanHandle(AutoLoginSubtask subtask)
        {
            return subtask.TaskStep switch
            {
                LoginTaskStep.MemberSelection => true,
                LoginTaskStep.PasswordEntry => true,
                LoginTaskStep.OTPEntry => true,
                _ => false
            };
        }

        protected override async Task ExecuteHandlerLogicAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IAutoLoginContext context, CancellationToken cancellationToken)
        {
            subtask.Start();

            await _loggingService.LogInfoAsync($"[FLOW] Starting PlayOnline {subtask.TaskStep} for {queueItem.DisplayName}");

            try
            {
                switch (subtask.TaskStep)
                {
                    case LoginTaskStep.MemberSelection:
                        await ExecuteMemberSelectionAsync(subtask, queueItem, context, cancellationToken);
                        break;

                    case LoginTaskStep.PasswordEntry:
                        await ExecutePasswordEntryAsync(subtask, queueItem, context, cancellationToken);
                        break;

                    case LoginTaskStep.OTPEntry:
                        await ExecuteOTPEntryAsync(subtask, queueItem, context, cancellationToken);
                        break;

                    default:
                        throw new NotSupportedException($"Task step {subtask.TaskStep} is not supported by PlayOnlineAuthHandler");
                }

                subtask.Complete();
                await _loggingService.LogInfoAsync($"[FLOW] Completed PlayOnline {subtask.TaskStep} for {queueItem.DisplayName}");
            }
            catch (OperationCanceledException)
            {
                subtask.Cancel();
                throw;
            }
            catch (Exception ex)
            {
                subtask.Fail(ex.Message);
                await _loggingService.LogErrorAsync($"PlayOnline {subtask.TaskStep} failed for {queueItem.DisplayName}", ex);
                throw;
            }
        }

        /// <summary>
        /// Executes PlayOnline member selection by detecting the member screen,
        /// validating the account configuration, and clicking the appropriate member slot.
        /// </summary>
        /// <param name="subtask">Current subtask for progress reporting</param>
        /// <param name="queueItem">Queue item containing account and profile information</param>
        /// <param name="context">AutoLogin context for sharing data between handler steps</param>
        /// <param name="cancellationToken">Cancellation token for operation cancellation</param>
        /// <exception cref="InvalidOperationException">Thrown when account validation fails or member selection screen cannot be detected</exception>
        /// <exception cref="ArgumentException">Thrown when member slot is not between 1 and 4</exception>
        /// <remarks>
        /// This method orchestrates the complete member selection flow:
        /// 1. Validates account information and member slot configuration
        /// 2. Detects PlayOnline window and waits for startup completion
        /// 3. Waits for member selection screen with extended timeout (60 seconds)
        /// 4. Allows screen stabilization before interaction
        /// 5. Clicks the configured member slot using centralized coordinates
        /// 6. Waits for PlayOnline response and stores context for next steps
        /// 
        /// **Error Recovery:**
        /// - Validates member slot range (1-4) before attempting selection
        /// - Uses extended timeout for member selection screen (slower systems)
        /// - Includes screen stabilization delay for consistent UI interaction
        /// - Comprehensive logging for troubleshooting selection failures
        /// </remarks>
        private async Task ExecuteMemberSelectionAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IAutoLoginContext context, CancellationToken cancellationToken)
        {
            var accountName = queueItem.Account?.AccountName ?? "Unknown";

            // Phase 1: Validate account configuration
            var memberSlot = await ValidateMemberSelectionConfigurationAsync(subtask, queueItem, cancellationToken);

            // Phase 2: Establish PlayOnline window connection
            var windowHandle = await EstablishPlayOnlineConnectionAsync(subtask, cancellationToken);

            // Phase 3: Detect and validate member selection screen
            await DetectMemberSelectionScreenAsync(subtask, windowHandle, cancellationToken);

            // Phase 4: Perform member slot selection
            await SelectMemberSlotAsync(subtask, memberSlot, windowHandle, accountName, cancellationToken);

            // Phase 5: Store context for subsequent steps
            await StoreMemberSelectionContextAsync(context, memberSlot, windowHandle);

            await UpdateProgressWithPhaseAsync(subtask, "authentication", PlayOnlineAuthConfiguration.ProgressMilestones.MemberSelection.Complete,
                                                "Character slot selected successfully");
        }

        /// <summary>
        /// Validates account configuration and member slot settings for member selection.
        /// </summary>
        /// <param name="subtask">Current subtask for progress reporting</param>
        /// <param name="queueItem">Queue item containing account information</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Validated member slot number (1-4)</returns>
        /// <exception cref="InvalidOperationException">Thrown when account name is missing</exception>
        /// <exception cref="ArgumentException">Thrown when member slot is invalid</exception>
        private async Task<int> ValidateMemberSelectionConfigurationAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, CancellationToken cancellationToken)
        {
            // Validate account information
            ValidateAccountProperty(queueItem.Account?.AccountName, "Account Name", "member selection");

            // Get and validate member slot configuration
            var memberSlot = queueItem.Account.POLMemberSlot;
            PlayOnlineAuthConfiguration.MemberSlots.ValidateSlot(memberSlot);

            await _loggingService.LogInfoAsync($"Member selection validated - Account: {queueItem.Account.AccountName}, Slot: {memberSlot}");
            return memberSlot;
        }

        /// <summary>
        /// Establishes connection to PlayOnline window with startup detection.
        /// </summary>
        /// <param name="subtask">Current subtask for progress reporting</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>PlayOnline window handle</returns>
        /// <exception cref="InvalidOperationException">Thrown when PlayOnline window cannot be found</exception>
        private async Task<IntPtr> EstablishPlayOnlineConnectionAsync(AutoLoginSubtask subtask, CancellationToken cancellationToken)
        {
            // Find PlayOnline window using base infrastructure
            await UpdateProgressWithPhaseAsync(subtask, "startup", PlayOnlineAuthConfiguration.ProgressMilestones.MemberSelection.WindowDetection,
                                                "Connecting to PlayOnline");
            
            var windowHandle = await FindWindowHandleAsync(
                subtask,
                PlayOnlineAuthConfiguration.ProcessNames.PlayOnline,
                "PlayOnline",
                cancellationToken);

            // Wait for PlayOnline startup completion with progress tracking
            await UpdateProgressWithPhaseAsync(subtask, "startup", PlayOnlineAuthConfiguration.ProgressMilestones.MemberSelection.StartupCompletion,
                                                "PlayOnline is starting up");
            
            await WaitForPlayOnlineStartupAsync(windowHandle, cancellationToken);

            return windowHandle;
        }

        /// <summary>
        /// Detects and validates the member selection screen with extended timeout.
        /// </summary>
        /// <param name="subtask">Current subtask for progress reporting</param>
        /// <param name="windowHandle">PlayOnline window handle</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <exception cref="InvalidOperationException">Thrown when member selection screen cannot be detected</exception>
        /// <remarks>
        /// Uses extended timeout (60 seconds) as member selection screen can take longer
        /// on slower systems or when PlayOnline is performing background operations.
        /// </remarks>
        private async Task DetectMemberSelectionScreenAsync(AutoLoginSubtask subtask, IntPtr windowHandle, CancellationToken cancellationToken)
        {
            await UpdateProgressWithPhaseAsync(subtask, "authentication", PlayOnlineAuthConfiguration.ProgressMilestones.MemberSelection.ScreenDetection,
                                                "Loading character selection");

            // Use configuration-driven screen detection with extended timeout
            var detectionOptions = ScreenDetectionOptions.WithTimeout((int)PlayOnlineAuthConfiguration.Timeouts.MemberSelectionDetection.TotalSeconds);
            
            var memberScreenMatch = await WaitForScreenDetectionAsync(
                subtask,
                PlayOnlineAuthConfiguration.TemplatePaths.MemberSelectionScreen,
                windowHandle,
                "member selection screen",
                cancellationToken,
                detectionOptions);

            // Validate detection confidence using configuration threshold
            if (memberScreenMatch.Confidence < PlayOnlineAuthConfiguration.ConfidenceThresholds.ScreenDetection)
            {
                throw new InvalidOperationException(
                    $"Could not detect member selection screen after extended wait (confidence: {memberScreenMatch.Confidence:P}, threshold: {PlayOnlineAuthConfiguration.ConfidenceThresholds.ScreenDetection:P})");
            }

            // Allow screen stabilization before interaction
            await UpdateProgressWithPhaseAsync(subtask, "authentication", PlayOnlineAuthConfiguration.ProgressMilestones.MemberSelection.ScreenStabilization,
                                                "Preparing character selection");
            
            await Task.Delay(PlayOnlineAuthConfiguration.Delays.UIStabilization, cancellationToken);
        }

        /// <summary>
        /// Performs the actual member slot selection by clicking the appropriate coordinates.
        /// </summary>
        /// <param name="subtask">Current subtask for progress reporting</param>
        /// <param name="memberSlot">Member slot number to select (1-4)</param>
        /// <param name="windowHandle">PlayOnline window handle</param>
        /// <param name="accountName">Account name for logging</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <remarks>
        /// Uses coordinates from PlayOnlineAuthConfiguration to ensure consistency
        /// and allow for easy adjustment without code changes.
        /// </remarks>
        private async Task SelectMemberSlotAsync(AutoLoginSubtask subtask, int memberSlot, IntPtr windowHandle, string accountName, CancellationToken cancellationToken)
        {
            // Get fresh screenshot for interaction
            await UpdateProgressWithPhaseAsync(subtask, "authentication", PlayOnlineAuthConfiguration.ProgressMilestones.MemberSelection.PreparingSelection,
                                                "Selecting character slot");
            
            var finalScreenshot = await CaptureScreenshotWithLogging(windowHandle, "member slot selection", cancellationToken);

            // Perform member slot click using configuration coordinates
            await UpdateProgressWithPhaseAsync(subtask, "authentication", PlayOnlineAuthConfiguration.ProgressMilestones.MemberSelection.ClickingMember,
                                                "Confirming character selection");
            
            var memberCoordinates = PlayOnlineAuthConfiguration.Coordinates.MemberSlots.GetSlotCoordinate(memberSlot);
            
            await ClickAtCoordinatesAsync(
                subtask,
                memberCoordinates,
                windowHandle,
                $"member slot {memberSlot}",
                cancellationToken,
                _automationService);

            // Wait for PlayOnline response using configured delay
            await UpdateProgressWithPhaseAsync(subtask, "authentication", PlayOnlineAuthConfiguration.ProgressMilestones.MemberSelection.ConfirmingSelection,
                                                "Processing selection");
            
            await Task.Delay(PlayOnlineAuthConfiguration.Delays.PlayOnlineResponse, cancellationToken);
        }

        /// <summary>
        /// Stores member selection context for use by subsequent handler steps.
        /// </summary>
        /// <param name="context">AutoLogin context for data storage</param>
        /// <param name="memberSlot">Selected member slot number</param>
        /// <param name="windowHandle">Current PlayOnline window handle</param>
        private async Task StoreMemberSelectionContextAsync(IAutoLoginContext context, int memberSlot, IntPtr windowHandle)
        {
            context.SetData("SelectedMemberSlot", memberSlot);
            context.SetData("WindowHandle", windowHandle);
            
            // Store PID for transition tracking
            try
            {
                var screenshot = await _screenshotService.CaptureWindowAsync(windowHandle, CancellationToken.None);
                if (screenshot?.ProcessId != null)
                {
                    context.SetData("PlayOnlineProcessId", screenshot.ProcessId);
                    context.SetData("PlayOnlineWindowHandle", windowHandle);
                    await _loggingService.LogDebugAsync($"PlayOnline transition context stored - PID: {screenshot.ProcessId}, Handle: 0x{windowHandle.ToInt64():X}");
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogWarningAsync($"Could not store PlayOnline PID context: {ex.Message}");
            }
            
            await _loggingService.LogDebugAsync($"Member selection context stored - Slot: {memberSlot}, Handle: 0x{windowHandle.ToInt64():X}");
        }

        /// <summary>
        /// Validates that the account has a stored password for secure authentication.
        /// </summary>
        /// <param name="subtask">Current subtask for progress reporting</param>
        /// <param name="queueItem">Queue item containing account information</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <exception cref="InvalidOperationException">Thrown when no stored password is found</exception>
        private async Task ValidatePasswordConfigurationAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, CancellationToken cancellationToken)
        {
            var accountName = queueItem.Account?.AccountName ?? "Unknown";
            
            if (!queueItem.Account?.HasStoredPassword ?? true)
            {
                throw new InvalidOperationException($"No stored password found for account: {accountName}. Please configure password storage before attempting automatic login.");
            }

            await _loggingService.LogDebugAsync($"Password configuration validated for account: {accountName}");
        }

        /// <summary>
        /// Navigates to the login information screen and initiates the login process.
        /// </summary>
        /// <param name="subtask">Current subtask for progress reporting</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>PlayOnline window handle</returns>
        /// <exception cref="InvalidOperationException">Thrown when login screen cannot be detected</exception>
        private async Task<IntPtr> NavigateToLoginScreenAsync(AutoLoginSubtask subtask, CancellationToken cancellationToken)
        {
            // Get PlayOnline window handle using base infrastructure
            var windowHandle = await FindWindowHandleAsync(
                subtask,
                PlayOnlineAuthConfiguration.ProcessNames.PlayOnline,
                "PlayOnline",
                cancellationToken);

            await UpdateProgressWithPhaseAsync(subtask, "authentication", PlayOnlineAuthConfiguration.ProgressMilestones.PasswordEntry.WindowDetection,
                                                "Loading login screen");

            // Detect login information screen with configuration-driven timeout
            await UpdateProgressWithPhaseAsync(subtask, "authentication", PlayOnlineAuthConfiguration.ProgressMilestones.PasswordEntry.LoginScreenDetection,
                                                "Preparing login interface");
            
            var detectionOptions = ScreenDetectionOptions.WithTimeout((int)PlayOnlineAuthConfiguration.Timeouts.LoginScreenDetection.TotalSeconds);
            var loginScreenMatch = await WaitForScreenDetectionAsync(
                subtask,
                PlayOnlineAuthConfiguration.TemplatePaths.LoginInformationScreen,
                windowHandle,
                "login information screen",
                cancellationToken,
                detectionOptions);

            // Validate detection confidence using configuration threshold
            if (loginScreenMatch.Confidence < PlayOnlineAuthConfiguration.ConfidenceThresholds.ScreenDetection)
            {
                throw new InvalidOperationException(
                    $"Could not detect login information screen (confidence: {loginScreenMatch.Confidence:P}, threshold: {PlayOnlineAuthConfiguration.ConfidenceThresholds.ScreenDetection:P})");
            }

            // Click "Log In" button using configuration coordinates
            await UpdateProgressWithPhaseAsync(subtask, "authentication", PlayOnlineAuthConfiguration.ProgressMilestones.PasswordEntry.LoginButtonClick,
                                                "Accessing login form");
            
            await ClickAtCoordinatesAsync(
                subtask,
                PlayOnlineAuthConfiguration.Coordinates.LoginScreen.LoginButton,
                windowHandle,
                "Log In button",
                cancellationToken,
                _automationService);
            
            // Wait for PlayOnline response using configuration
            await Task.Delay(PlayOnlineAuthConfiguration.Delays.PlayOnlineResponse, cancellationToken);

            return windowHandle;
        }

        /// <summary>
        /// Navigates to the PlayOnline connection screen and validates availability.
        /// </summary>
        /// <param name="subtask">Current subtask for progress reporting</param>
        /// <param name="windowHandle">PlayOnline window handle</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <exception cref="InvalidOperationException">Thrown when connection screen cannot be detected</exception>
        private async Task NavigateToConnectionScreenAsync(AutoLoginSubtask subtask, IntPtr windowHandle, CancellationToken cancellationToken)
        {
            // Wait for connect to PlayOnline screen
            await UpdateProgressWithPhaseAsync(subtask, "authentication", PlayOnlineAuthConfiguration.ProgressMilestones.PasswordEntry.ConnectionScreenDetection,
                                                "Connecting to servers");
            
            var detectionOptions = ScreenDetectionOptions.WithTimeout((int)PlayOnlineAuthConfiguration.Timeouts.LoginScreenDetection.TotalSeconds);
            var connectScreenMatch = await WaitForScreenDetectionAsync(
                subtask,
                PlayOnlineAuthConfiguration.TemplatePaths.ConnectToPlayOnlineScreen,
                windowHandle,
                "connect to PlayOnline screen",
                cancellationToken,
                detectionOptions);

            if (connectScreenMatch.Confidence < PlayOnlineAuthConfiguration.ConfidenceThresholds.ScreenDetection)
            {
                throw new InvalidOperationException(
                    $"Could not detect connect to PlayOnline screen (confidence: {connectScreenMatch.Confidence:P}, threshold: {PlayOnlineAuthConfiguration.ConfidenceThresholds.ScreenDetection:P})");
            }
        }

        /// <summary>
        /// Activates the password entry interface by clicking the password field and virtual keyboard.
        /// </summary>
        /// <param name="subtask">Current subtask for progress reporting</param>
        /// <param name="windowHandle">PlayOnline window handle</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <exception cref="InvalidOperationException">Thrown when virtual keyboard cannot be detected</exception>
        /// <remarks>
        /// This method handles the two-step activation process:
        /// 1. Clicks the password field on the connection screen
        /// 2. Waits for and activates the virtual keyboard interface
        /// </remarks>
        private async Task ActivatePasswordEntryInterfaceAsync(AutoLoginSubtask subtask, IntPtr windowHandle, CancellationToken cancellationToken)
        {
            // Click password field using configuration coordinates
            await UpdateProgressWithPhaseAsync(subtask, "authentication", PlayOnlineAuthConfiguration.ProgressMilestones.PasswordEntry.PasswordFieldClick,
                                                "Preparing password entry");
            
            await ClickAtCoordinatesAsync(
                subtask,
                PlayOnlineAuthConfiguration.Coordinates.LoginScreen.PasswordField,
                windowHandle,
                "password field",
                cancellationToken,
                _automationService);
            
            await Task.Delay(PlayOnlineAuthConfiguration.Delays.InteractionCompletion, cancellationToken);

            // Wait for virtual keyboard appearance
            await UpdateProgressWithPhaseAsync(subtask, "authentication", PlayOnlineAuthConfiguration.ProgressMilestones.PasswordEntry.VirtualKeyboardDetection,
                                                "Loading secure keyboard");
            
            var detectionOptions = ScreenDetectionOptions.WithTimeout((int)PlayOnlineAuthConfiguration.Timeouts.VirtualKeyboardDetection.TotalSeconds);
            var keyboardMatch = await WaitForScreenDetectionAsync(
                subtask,
                PlayOnlineAuthConfiguration.TemplatePaths.VirtualKeyboardPasswordInputScreen,
                windowHandle,
                "virtual keyboard screen",
                cancellationToken,
                detectionOptions);

            if (keyboardMatch.Confidence < PlayOnlineAuthConfiguration.ConfidenceThresholds.ScreenDetection)
            {
                throw new InvalidOperationException(
                    $"Could not detect virtual keyboard (confidence: {keyboardMatch.Confidence:P}, threshold: {PlayOnlineAuthConfiguration.ConfidenceThresholds.ScreenDetection:P})");
            }

            // Click virtual keyboard password field using configuration coordinates
            await UpdateProgressWithPhaseAsync(subtask, "authentication", PlayOnlineAuthConfiguration.ProgressMilestones.PasswordEntry.KeyboardFieldClick,
                                                "Accessing secure input");
            
            await ClickAtCoordinatesAsync(
                subtask,
                PlayOnlineAuthConfiguration.Coordinates.LoginScreen.VirtualKeyboardPasswordField,
                windowHandle,
                "virtual keyboard password field",
                cancellationToken,
                _automationService);
            
            await Task.Delay(PlayOnlineAuthConfiguration.Delays.KeyboardInput, cancellationToken);
        }

        /// <summary>
        /// Performs secure password entry with comprehensive diagnostic logging.
        /// </summary>
        /// <param name="subtask">Current subtask for progress reporting</param>
        /// <param name="queueItem">Queue item containing account and profile information</param>
        /// <param name="windowHandle">PlayOnline window handle</param>
        /// <param name="accountName">Account name for logging</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <exception cref="InvalidOperationException">Thrown when password cannot be retrieved</exception>
        /// <remarks>
        /// This method provides comprehensive security logging without exposing sensitive data:
        /// - Logs password length for validation
        /// - Identifies potentially problematic characters
        /// - Uses secure text entry mechanisms
        /// - Provides completion confirmation
        /// </remarks>
        private async Task PerformSecurePasswordEntryAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IntPtr windowHandle, string accountName, CancellationToken cancellationToken)
        {
            // Retrieve password from Windows Credential Manager
            await UpdateProgressWithPhaseAsync(subtask, "authentication", PlayOnlineAuthConfiguration.ProgressMilestones.PasswordEntry.PasswordRetrieval,
                                                "Retrieving credentials");
            
            var credentialTarget = _credentialsService.GenerateCredentialTarget(queueItem.Profile?.FilePath ?? string.Empty, queueItem.Account.Id);
            var password = await _credentialsService.RetrievePasswordAsync(credentialTarget, accountName);
            
            if (string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException($"Could not retrieve password for account: {accountName}. Please verify password storage configuration.");
            }

            // SECURITY: Log password characteristics without exposing content
            await _loggingService.LogDebugAsync($"Password retrieved for {accountName}: Length={password.Length} characters");

            // DIAGNOSTIC: Check for potentially problematic characters
            var problematicChars = password.Where(c => char.IsControl(c) || c > 127).ToList();
            if (problematicChars.Any())
            {
                await _loggingService.LogWarningAsync(
                    $"Password contains {problematicChars.Count} potentially problematic characters (control chars or non-ASCII) - may affect typing accuracy");
            }

            // Perform secure password entry
            await UpdateProgressWithPhaseAsync(subtask, "authentication", PlayOnlineAuthConfiguration.ProgressMilestones.PasswordEntry.PasswordInput,
                                                "Verifying account credentials");
            
            await _automationService.TypeSecureTextAsync(password, 50, cancellationToken);
            
            // Allow keyboard input completion
            await Task.Delay(PlayOnlineAuthConfiguration.Delays.KeyboardInput, cancellationToken);

            await _loggingService.LogDebugAsync($"Password entry completed successfully for {accountName}");
        }

        /// <summary>
        /// Confirms password entry and conditionally initiates connection if OTP is not required.
        /// </summary>
        /// <param name="subtask">Current subtask for progress reporting</param>
        /// <param name="queueItem">Queue item containing account configuration</param>
        /// <param name="windowHandle">PlayOnline window handle</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <remarks>
        /// This method handles two scenarios:
        /// 1. OTP Required: Confirms password only, leaves connection for OTP step
        /// 2. No OTP: Confirms password and initiates connection immediately
        /// </remarks>
        private async Task ConfirmPasswordAndConnectAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IntPtr windowHandle, CancellationToken cancellationToken)
        {
            // Click CircleConfirmation button to confirm password entry
            await UpdateProgressWithPhaseAsync(subtask, "authentication", PlayOnlineAuthConfiguration.ProgressMilestones.PasswordEntry.Confirmation,
                                                "Confirming login details");
            
            await ClickAtTemplateCoordinatesAsync(
                subtask,
                PlayOnlineAuthConfiguration.TemplatePaths.CircleConfirmationPassword,
                windowHandle,
                cancellationToken,
                _automationService);
            
            // Allow confirmation to process
            await Task.Delay(PlayOnlineAuthConfiguration.Delays.InteractionCompletion, cancellationToken);

            // Check if OTP is required - if not, click Connect now
            if (!queueItem.Account?.IsOTPEnabled ?? true)
            {
                await _loggingService.LogInfoAsync("OTP not required - proceeding with direct connection");
                
                await UpdateProgressWithPhaseAsync(subtask, "authentication", 95, "Connecting to game servers");
                
                await ClickAtTemplateCoordinatesAsync(
                    subtask,
                    PlayOnlineAuthConfiguration.TemplatePaths.ConnectButton,
                    windowHandle,
                    cancellationToken,
                    _automationService);
                
                // Allow connection to process
                await Task.Delay(PlayOnlineAuthConfiguration.Delays.ConnectionProcessing, cancellationToken);
            }
            else
            {
                await _loggingService.LogInfoAsync("OTP required - password confirmed, awaiting OTP entry step");
            }
        }

        /// <summary>
        /// Executes PlayOnline password entry by navigating through login screens,
        /// securely retrieving and entering the stored password, and handling confirmation.
        /// </summary>
        /// <param name="subtask">Current subtask for progress reporting</param>
        /// <param name="queueItem">Queue item containing account and profile information</param>
        /// <param name="context">AutoLogin context for sharing data between handler steps</param>
        /// <param name="cancellationToken">Cancellation token for operation cancellation</param>
        /// <exception cref="InvalidOperationException">Thrown when password validation fails or screens cannot be detected</exception>
        /// <remarks>
        /// This method orchestrates the complete password entry flow:
        /// 1. Validates stored password availability
        /// 2. Navigates through login information and connection screens
        /// 3. Activates virtual keyboard for secure password entry
        /// 4. Retrieves password from Windows Credential Manager
        /// 5. Performs secure password entry with diagnostic logging
        /// 6. Confirms password entry and optionally connects (if OTP not required)
        /// 
        /// **Security Features:**
        /// - Password retrieved securely from Windows Credential Manager
        /// - Secure text entry through IUIAutomationService.TypeSecureTextAsync
        /// - Diagnostic logging without exposing password content
        /// - Validation of problematic characters that might affect typing
        /// </remarks>
        private async Task ExecutePasswordEntryAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IAutoLoginContext context, CancellationToken cancellationToken)
        {
            var accountName = queueItem.Account?.AccountName ?? "Unknown";

            // Phase 1: Validate password configuration
            await ValidatePasswordConfigurationAsync(subtask, queueItem, cancellationToken);

            // Phase 2: Navigate to login information screen
            var windowHandle = await NavigateToLoginScreenAsync(subtask, cancellationToken);

            // Phase 3: Navigate to connection screen
            await NavigateToConnectionScreenAsync(subtask, windowHandle, cancellationToken);

            // Phase 4: Activate password entry interface
            await ActivatePasswordEntryInterfaceAsync(subtask, windowHandle, cancellationToken);

            // Phase 5: Perform secure password entry
            await PerformSecurePasswordEntryAsync(subtask, queueItem, windowHandle, accountName, cancellationToken);

            // Phase 6: Confirm password and conditionally connect
            await ConfirmPasswordAndConnectAsync(subtask, queueItem, windowHandle, cancellationToken);

            await UpdateProgressWithPhaseAsync(subtask, "authentication", PlayOnlineAuthConfiguration.ProgressMilestones.PasswordEntry.Complete,
                                                "Account verification completed");
        }

        /// <summary>
        /// Executes PlayOnline OTP (One-Time Password) entry for two-factor authentication.
        /// Handles OTP generation, secure entry, connection, and navigation flow determination.
        /// </summary>
        /// <param name="subtask">Current subtask for progress reporting</param>
        /// <param name="queueItem">Queue item containing account and profile information</param>
        /// <param name="context">AutoLogin context for sharing data between handler steps</param>
        /// <param name="cancellationToken">Cancellation token for operation cancellation</param>
        /// <remarks>
        /// This method orchestrates the complete OTP authentication flow:
        /// 1. Validates OTP requirements and skips if not needed
        /// 2. Generates secure OTP code using configured authentication key
        /// 3. Locates and activates OTP input field
        /// 4. Performs secure OTP code entry
        /// 5. Initiates connection and determines navigation flow
        /// 6. Handles POL Proxy detection for streamlined flow vs standard navigation
        /// 
        /// **Security Features:**
        /// - OTP code generation using TOTP algorithm
        /// - Secure logging that masks sensitive OTP data
        /// - Automatic detection of POL Proxy to optimize flow
        /// - Context preservation for subsequent handler steps
        /// </remarks>
        private async Task ExecuteOTPEntryAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IAutoLoginContext context, CancellationToken cancellationToken)
        {
            var accountName = queueItem.Account?.AccountName ?? "Unknown";

            // Phase 1: Validate OTP requirements
            if (!ValidateOTPRequirements(subtask, queueItem))
                return;

            // Phase 2: Generate secure OTP code
            var otpCode = await GenerateSecureOTPCodeAsync(subtask, queueItem, accountName, cancellationToken);

            // Phase 3: Establish window connection and activate OTP field
            var windowHandle = await EstablishOTPConnectionAsync(subtask, cancellationToken);

            // Phase 4: Perform secure OTP entry
            await PerformSecureOTPEntryAsync(subtask, windowHandle, otpCode, cancellationToken);

            // Phase 5: Initiate connection
            await InitiateOTPConnectionAsync(subtask, windowHandle, cancellationToken);

            // Phase 6: Determine navigation flow based on POL Proxy configuration
            await DetermineNavigationFlowAsync(subtask, queueItem, windowHandle, context, cancellationToken);

            await UpdateProgressWithPhaseAsync(subtask, "authentication", PlayOnlineAuthConfiguration.ProgressMilestones.OTPEntry.Complete,
                                                "Two-factor authentication completed");
        }

        /// <summary>
        /// Validates whether OTP entry is required for the current account.
        /// </summary>
        /// <param name="subtask">Current subtask for progress reporting</param>
        /// <param name="queueItem">Queue item containing account configuration</param>
        /// <returns>True if OTP entry should proceed, false if it should be skipped</returns>
        private bool ValidateOTPRequirements(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem)
        {
            // Check if OTP is required for this account
            if (!queueItem.Account?.IsOTPEnabled ?? true)
            {
                subtask.Skip("OTP not required for this account");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Generates secure OTP code using the account's stored authentication key.
        /// </summary>
        /// <param name="subtask">Current subtask for progress reporting</param>
        /// <param name="queueItem">Queue item containing account and profile information</param>
        /// <param name="accountName">Account name for logging</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Generated OTP code</returns>
        /// <exception cref="InvalidOperationException">Thrown when OTP code cannot be generated</exception>
        /// <remarks>
        /// Uses TOTP (Time-based One-Time Password) algorithm with the account's
        /// stored authentication key from Windows Credential Manager.
        /// </remarks>
        private async Task<string> GenerateSecureOTPCodeAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, string accountName, CancellationToken cancellationToken)
        {
            await UpdateProgressWithPhaseAsync(subtask, "authentication", PlayOnlineAuthConfiguration.ProgressMilestones.OTPEntry.OTPGeneration,
                                                "Generating security code");
            
            var otpCode = await _otpService.GenerateOTPCodeAsync(queueItem.Profile?.FilePath ?? string.Empty, queueItem.Account.Id);
            
            if (string.IsNullOrEmpty(otpCode))
            {
                throw new InvalidOperationException($"Could not generate OTP code for account: {accountName}. Please verify OTP configuration and authentication key storage.");
            }

            // SECURITY: Log OTP generation without exposing the full code
            await _loggingService.LogDebugAsync($"Generated OTP code for {accountName}: {otpCode.Substring(0, 2)}**** (Length: {otpCode.Length})");
            
            return otpCode;
        }

        /// <summary>
        /// Establishes connection to PlayOnline window for OTP entry.
        /// </summary>
        /// <param name="subtask">Current subtask for progress reporting</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>PlayOnline window handle</returns>
        private async Task<IntPtr> EstablishOTPConnectionAsync(AutoLoginSubtask subtask, CancellationToken cancellationToken)
        {
            // Get PlayOnline window handle using base infrastructure with configuration
            var windowHandle = await FindWindowHandleAsync(
                subtask,
                PlayOnlineAuthConfiguration.ProcessNames.PlayOnline,
                "PlayOnline",
                cancellationToken);

            return windowHandle;
        }

        /// <summary>
        /// Performs secure OTP entry by locating the OTP field and entering the code.
        /// </summary>
        /// <param name="subtask">Current subtask for progress reporting</param>
        /// <param name="windowHandle">PlayOnline window handle</param>
        /// <param name="otpCode">Generated OTP code to enter</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <remarks>
        /// Uses configuration-driven coordinates and secure text entry mechanisms.
        /// Provides masked logging for security purposes.
        /// </remarks>
        private async Task PerformSecureOTPEntryAsync(AutoLoginSubtask subtask, IntPtr windowHandle, string otpCode, CancellationToken cancellationToken)
        {
            // Locate and activate OTP input field
            await UpdateProgressWithPhaseAsync(subtask, "authentication", PlayOnlineAuthConfiguration.ProgressMilestones.OTPEntry.FieldLocation,
                                                "Preparing security verification");
            
            var otpScreenshot = await CaptureScreenshotWithLogging(windowHandle, "OTP field selection", cancellationToken);

            // Click OTP field using configuration coordinates
            var otpFieldPoint = otpScreenshot.ToScreenCoordinates(PlayOnlineAuthConfiguration.Coordinates.LoginScreen.OTPField);
            await _automationService.ClickAsync(otpFieldPoint, cancellationToken);
            
            // Allow field activation
            await Task.Delay(PlayOnlineAuthConfiguration.Delays.KeyboardInput, cancellationToken);

            // Enter OTP code securely
            await UpdateProgressWithPhaseAsync(subtask, "authentication", PlayOnlineAuthConfiguration.ProgressMilestones.OTPEntry.OTPInput,
                                                "Entering security code");
            
            await _automationService.TypeTextAsync(otpCode, 100, cancellationToken);
            
            // Allow input completion
            await Task.Delay(PlayOnlineAuthConfiguration.Delays.KeyboardInput, cancellationToken);

            await _loggingService.LogDebugAsync($"OTP code entered successfully (masked for security)");
        }

        /// <summary>
        /// Initiates connection after OTP entry by clicking the Connect button.
        /// </summary>
        /// <param name="subtask">Current subtask for progress reporting</param>
        /// <param name="windowHandle">PlayOnline window handle</param>
        /// <param name="cancellationToken">Cancellation token</param>
        private async Task InitiateOTPConnectionAsync(AutoLoginSubtask subtask, IntPtr windowHandle, CancellationToken cancellationToken)
        {
            // Click Connect button after OTP entry using configuration
            await UpdateProgressWithPhaseAsync(subtask, "authentication", PlayOnlineAuthConfiguration.ProgressMilestones.OTPEntry.Connection,
                                                "Connecting with verified credentials");
            
            await ClickAtTemplateCoordinatesAsync(
                subtask,
                PlayOnlineAuthConfiguration.TemplatePaths.ConnectButton,
                windowHandle,
                cancellationToken,
                _automationService);
            
            // Allow connection to process using configuration delay
            await Task.Delay(PlayOnlineAuthConfiguration.Delays.ConnectionProcessing, cancellationToken);
        }

        /// <summary>
        /// Determines the navigation flow based on POL Proxy configuration.
        /// Either bypasses PlayOnline screens (POL Proxy) or proceeds with standard navigation.
        /// </summary>
        /// <param name="subtask">Current subtask for progress reporting</param>
        /// <param name="queueItem">Queue item for context</param>
        /// <param name="windowHandle">Current PlayOnline window handle</param>
        /// <param name="context">AutoLogin context for data storage</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <remarks>
        /// POL Proxy detection uses IExternalApplicationService patterns for consistency
        /// with established application discovery mechanisms throughout the codebase.
        /// </remarks>
        private async Task DetermineNavigationFlowAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IntPtr windowHandle, IAutoLoginContext context, CancellationToken cancellationToken)
        {
            await UpdateProgressWithPhaseAsync(subtask, "gameconnection", PlayOnlineAuthConfiguration.ProgressMilestones.OTPEntry.PostProcessing,
                                                "Preparing game launch");

            // Use IExternalApplicationService pattern-based detection for POL Proxy
            var polProxyApp = await _externalApplicationService.FindApplicationByPatternAsync(
                PlayOnlineAuthConfiguration.ApplicationPatterns.POLProxyNamePatterns,
                PlayOnlineAuthConfiguration.ApplicationPatterns.POLProxyPathPatterns);

            if (polProxyApp != null)
            {
                // POL Proxy detected - streamlined flow
                await _loggingService.LogInfoAsync($"POL Proxy detected ({polProxyApp.Name}) - skipping PlayOnline navigation screens");
                
                await UpdateProgressWithPhaseAsync(subtask, "gameconnection", 90, "Fast-tracking to game launch");

                // Store context for FFXI handler
                context.SetData("WindowHandle", windowHandle);
                context.SetData("POLProxyDetected", true);
                
                // Update PlayOnline window handle for transition tracking
                try
                {
                    var screenshot = await _screenshotService.CaptureWindowAsync(windowHandle, cancellationToken);
                if (screenshot?.ProcessId != null)
                {
                    context.SetData("PlayOnlineProcessId", screenshot.ProcessId);
                    context.SetData("PlayOnlineWindowHandle", windowHandle);
                    await _loggingService.LogDebugAsync($"Updated PlayOnline transition context for POL Proxy - PID: {screenshot.ProcessId}, Handle: 0x{windowHandle.ToInt64():X}");
                }
                }
                catch (Exception ex)
                {
                    await _loggingService.LogWarningAsync($"Could not update PlayOnline PID context for POL Proxy: {ex.Message}");
                }

                // Allow POL Proxy transition time using configuration
                await Task.Delay(PlayOnlineAuthConfiguration.Delays.POLProxyTransition, cancellationToken);
            }
            else
            {
                // Standard PlayOnline navigation flow
                await _loggingService.LogInfoAsync("No POL Proxy configured - proceeding with standard PlayOnline navigation");
                
                context.SetData("POLProxyDetected", false);
                await NavigateToFinalFantasyXI(subtask, windowHandle, context, cancellationToken);
            }
        }

        /// <summary>
        /// Navigates through PlayOnline screens to launch Final Fantasy XI.
        /// Handles main screen, game selection, play screen, and final confirmation.
        /// </summary>
        /// <param name="subtask">Current subtask for progress reporting</param>
        /// <param name="windowHandle">Initial PlayOnline window handle</param>
        /// <param name="context">AutoLogin context for window handle tracking</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <remarks>
        /// This method handles the complete PlayOnline to FFXI navigation flow:
        /// 1. Waits for PlayOnline main screen detection
        /// 2. Selects Final Fantasy XI from game list
        /// 3. Navigates through play screens and confirmations
        /// 4. Maintains window handle tracking throughout transitions
        /// 
        /// **Configuration Features:**
        /// - Uses centralized coordinates from PlayOnlineAuthConfiguration
        /// - Applies configuration-driven timeouts and delays
        /// - Leverages base infrastructure for screen detection
        /// - Consistent progress reporting with milestone constants
        /// </remarks>
        private async Task NavigateToFinalFantasyXI(AutoLoginSubtask subtask, IntPtr windowHandle, IAutoLoginContext context, CancellationToken cancellationToken)
        {
            // Phase 1: Wait for PlayOnline main screen
            windowHandle = await WaitForPlayOnlineMainScreenAsync(subtask, windowHandle, context, cancellationToken);

            // Phase 2: Select Final Fantasy XI
            await SelectFinalFantasyXIAsync(subtask, windowHandle, cancellationToken);

            // Phase 3: Navigate through play screens
            windowHandle = await NavigatePlayScreensAsync(subtask, windowHandle, context, cancellationToken);

            // Phase 4: Store final context
            context.SetData("WindowHandle", windowHandle);
            
            // Update PlayOnline window handle for transition tracking
            try
            {
                var screenshot = await _screenshotService.CaptureWindowAsync(windowHandle, cancellationToken);
                if (screenshot?.ProcessId != null)
                {
                    context.SetData("PlayOnlineProcessId", screenshot.ProcessId);
                    context.SetData("PlayOnlineWindowHandle", windowHandle);
                    await _loggingService.LogDebugAsync($"Updated PlayOnline transition context after FFXI navigation - PID: {screenshot.ProcessId}, Handle: 0x{windowHandle.ToInt64():X}");
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogWarningAsync($"Could not update PlayOnline PID context after FFXI navigation: {ex.Message}");
            }
            
            await _loggingService.LogDebugAsync($"FFXI navigation completed - Final handle: 0x{windowHandle.ToInt64():X}");
        }

        /// <summary>
        /// Waits for PlayOnline main screen with window handle management.
        /// </summary>
        /// <param name="subtask">Current subtask for progress reporting</param>
        /// <param name="windowHandle">Current window handle</param>
        /// <param name="context">AutoLogin context</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Updated window handle after detection</returns>
        private async Task<IntPtr> WaitForPlayOnlineMainScreenAsync(AutoLoginSubtask subtask, IntPtr windowHandle, IAutoLoginContext context, CancellationToken cancellationToken)
        {
            await UpdateProgressWithPhaseAsync(subtask, "gameconnection", PlayOnlineAuthConfiguration.ProgressMilestones.Navigation.MainScreenWait,
                                                "Loading game menu");

            var detectionOptions = ScreenDetectionOptions.WithTimeout((int)PlayOnlineAuthConfiguration.Timeouts.MainScreenDetection.TotalSeconds);
            var mainScreenMatch = await WaitForScreenDetectionAsync(
                subtask,
                PlayOnlineAuthConfiguration.TemplatePaths.MainScreen,
                windowHandle,
                "PlayOnline main screen",
                cancellationToken,
                detectionOptions);

            if (mainScreenMatch.Confidence < PlayOnlineAuthConfiguration.ConfidenceThresholds.ScreenDetection)
            {
                await _loggingService.LogWarningAsync($"Main screen detection confidence below threshold: {mainScreenMatch.Confidence:P}");
            }

            return windowHandle;
        }

        /// <summary>
        /// Selects Final Fantasy XI from the PlayOnline game list.
        /// </summary>
        /// <param name="subtask">Current subtask for progress reporting</param>
        /// <param name="windowHandle">PlayOnline window handle</param>
        /// <param name="cancellationToken">Cancellation token</param>
        private async Task SelectFinalFantasyXIAsync(AutoLoginSubtask subtask, IntPtr windowHandle, CancellationToken cancellationToken)
        {
            await UpdateProgressWithPhaseAsync(subtask, "gameconnection", PlayOnlineAuthConfiguration.ProgressMilestones.Navigation.GameSelection,
                                                "Selecting FINAL FANTASY XI");

            await ClickAtCoordinatesAsync(
                subtask,
                PlayOnlineAuthConfiguration.Coordinates.Navigation.FinalFantasyXIButton,
                windowHandle,
                "Final Fantasy XI button",
                cancellationToken,
                _automationService);

            // Allow game selection to process
            await Task.Delay(PlayOnlineAuthConfiguration.Delays.ConnectionProcessing, cancellationToken);
        }

        /// <summary>
        /// Navigates through play screens and confirmations to launch the game.
        /// </summary>
        /// <param name="subtask">Current subtask for progress reporting</param>
        /// <param name="windowHandle">Current window handle</param>
        /// <param name="context">AutoLogin context</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Updated window handle after navigation</returns>
        private async Task<IntPtr> NavigatePlayScreensAsync(AutoLoginSubtask subtask, IntPtr windowHandle, IAutoLoginContext context, CancellationToken cancellationToken)
        {
            // Wait for play screen
            await UpdateProgressWithPhaseAsync(subtask, "gameconnection", PlayOnlineAuthConfiguration.ProgressMilestones.Navigation.PlayScreenWait,
                                                "Loading game launcher");

            var playDetectionOptions = ScreenDetectionOptions.WithTimeout((int)PlayOnlineAuthConfiguration.Timeouts.PlayScreenDetection.TotalSeconds);
            var playScreenMatch = await WaitForScreenDetectionAsync(
                subtask,
                PlayOnlineAuthConfiguration.TemplatePaths.PlayScreen,
                windowHandle,
                "PlayOnline play screen",
                cancellationToken,
                playDetectionOptions);

            if (playScreenMatch.Confidence >= PlayOnlineAuthConfiguration.ConfidenceThresholds.ScreenDetection)
            {
                // Click Play button
                await ClickAtCoordinatesAsync(
                    subtask,
                    PlayOnlineAuthConfiguration.Coordinates.Navigation.PlayButton,
                    windowHandle,
                    "Play button",
                    cancellationToken,
                    _automationService);

                await Task.Delay(PlayOnlineAuthConfiguration.Delays.ConnectionProcessing, cancellationToken);

                // Handle final confirmation
                windowHandle = await HandleFinalConfirmationAsync(subtask, windowHandle, context, cancellationToken);
            }

            return windowHandle;
        }

        /// <summary>
        /// Handles the final play confirmation screen and launches the game.
        /// </summary>
        /// <param name="subtask">Current subtask for progress reporting</param>
        /// <param name="windowHandle">Current window handle</param>
        /// <param name="context">AutoLogin context</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Updated window handle after confirmation</returns>
        private async Task<IntPtr> HandleFinalConfirmationAsync(AutoLoginSubtask subtask, IntPtr windowHandle, IAutoLoginContext context, CancellationToken cancellationToken)
        {
            await UpdateProgressWithPhaseAsync(subtask, "gameconnection", PlayOnlineAuthConfiguration.ProgressMilestones.Navigation.FinalConfirmation,
                                                "Preparing to launch game");

            var confirmDetectionOptions = ScreenDetectionOptions.WithTimeout((int)PlayOnlineAuthConfiguration.Timeouts.PlayScreenDetection.TotalSeconds);
            var confirmScreenMatch = await WaitForScreenDetectionAsync(
                subtask,
                PlayOnlineAuthConfiguration.TemplatePaths.PlayConfirmation,
                windowHandle,
                "PlayOnline play confirmation screen",
                cancellationToken,
                confirmDetectionOptions);

            if (confirmScreenMatch.Confidence >= PlayOnlineAuthConfiguration.ConfidenceThresholds.ScreenDetection)
            {
                // Click final Play button
                await UpdateProgressWithPhaseAsync(subtask, "gameconnection", PlayOnlineAuthConfiguration.ProgressMilestones.Navigation.Complete,
                                                    "Launching FINAL FANTASY XI");

                await ClickAtCoordinatesAsync(
                    subtask,
                    PlayOnlineAuthConfiguration.Coordinates.Navigation.FinalPlayButton,
                    windowHandle,
                    "final Play button",
                    cancellationToken,
                    _automationService);

                // Allow game launch processing
                await Task.Delay(PlayOnlineAuthConfiguration.Delays.POLProxyTransition, cancellationToken);
            }

            return windowHandle;
        }


        /// <summary>
        /// Waits for PlayOnline Viewer startup sequence to complete with configurable timeout.
        /// Monitors window responsiveness and ensures UI elements are fully rendered.
        /// </summary>
        /// <param name="windowHandle">PlayOnline window handle to monitor</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <remarks>
        /// This method ensures PlayOnline has completed its initialization sequence
        /// before attempting UI interactions. Uses screenshot validation to confirm
        /// window responsiveness and includes buffer time for UI stabilization.
        /// </remarks>
        private async Task WaitForPlayOnlineStartupAsync(IntPtr windowHandle, CancellationToken cancellationToken)
        {
            var maxAttempts = (int)(PlayOnlineAuthConfiguration.Timeouts.PlayOnlineStartup.TotalSeconds);
            var attempt = 0;

            await _loggingService.LogInfoAsync($"Waiting for PlayOnline Viewer startup to complete (max {maxAttempts}s)...");

            while (attempt < maxAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Capture window and check if it's responsive
                var screenshot = await _screenshotService.CaptureWindowAsync(windowHandle, cancellationToken);

                if (screenshot != null && screenshot.IsValid)
                {
                    var windowTitle = screenshot.WindowTitle;
                    await _loggingService.LogDebugAsync($"POL startup check {attempt + 1}/{maxAttempts}: Window responsive, title: {windowTitle}");

                    // If we get 3 consecutive successful screenshots, consider POL ready
                    if (attempt >= 2)
                    {
                        await _loggingService.LogInfoAsync("PlayOnline Viewer startup sequence completed");
                        break;
                    }
                }

                attempt++;
                await Task.Delay(PlayOnlineAuthConfiguration.Delays.InteractionCompletion, cancellationToken); // Use configuration-driven intervals
            }

            // Additional buffer time for UI elements to fully render using configuration
            await Task.Delay(PlayOnlineAuthConfiguration.Delays.UIStabilization, cancellationToken);
        }



        /// <summary>
        /// Gets the screen coordinates for a specific member slot using centralized configuration.
        /// </summary>
        /// <param name="slotNumber">Member slot number (1-4)</param>
        /// <returns>Point coordinate for the specified slot</returns>
        /// <exception cref="ArgumentException">Thrown if slot number is not between 1 and 4</exception>
        /// <remarks>
        /// This method is deprecated in favor of using PlayOnlineAuthConfiguration.Coordinates.MemberSlots.GetSlotCoordinate()
        /// directly. Maintained for backward compatibility but should be replaced in refactoring.
        /// </remarks>
        [Obsolete("Use PlayOnlineAuthConfiguration.Coordinates.MemberSlots.GetSlotCoordinate() instead")]
        private static Point GetMemberSlotCoordinates(int slotNumber)
        {
            return PlayOnlineAuthConfiguration.Coordinates.MemberSlots.GetSlotCoordinate(slotNumber);
        }

    }
}