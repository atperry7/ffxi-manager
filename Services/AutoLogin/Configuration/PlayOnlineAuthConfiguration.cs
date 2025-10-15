using System;
using System.Drawing;

namespace FFXIManager.Services.AutoLogin.Configuration
{
    /// <summary>
    /// Configuration constants for PlayOnline authentication operations.
    /// Centralizes timeout values, coordinates, process names, and other configuration settings
    /// to improve maintainability and eliminate magic numbers throughout the handler.
    /// 
    /// This configuration follows the established pattern set by WindowerLaunchConfiguration
    /// and provides centralized management of all hardcoded values previously scattered
    /// throughout the PlayOnlineAuthHandler implementation.
    /// </summary>
    /// <remarks>
    /// **Extracted Constants Summary:**
    /// - Screen coordinates from ExecutePasswordEntryAsync: (305, 415), (1034, 520), (740, 390), (1034, 579)
    /// - Navigation coordinates from NavigateToFinalFantasyXI: (300, 410), (360, 240), (495, 940)
    /// - Member slot coordinates from GetMemberSlotCoordinates: Member slots 1-4
    /// - Timeout values: 1000ms, 1500ms, 2000ms, 3000ms delays throughout methods
    /// - Progress milestones: 5, 15, 30, 50, 70, 85, 100% progress markers
    /// - Confidence thresholds: 0.80f for screen detection
    /// - Member slot validation: 1-4 valid range
    /// - Process names: "pol", "ffximain" for process detection
    /// - Template paths: All PlayOnline screen detection templates
    /// </remarks>
    public static class PlayOnlineAuthConfiguration
    {
        /// <summary>
        /// Timeout values for various PlayOnline authentication operations.
        /// These values are based on typical PlayOnline response times and can be
        /// adjusted based on system performance and network conditions.
        /// </summary>
        public static class Timeouts
        {
            /// <summary>
            /// Maximum time to wait for PlayOnline startup sequence to complete.
            /// Default: 30 seconds - allows for POL initialization and UI loading.
            /// Used by: WaitForPlayOnlineStartup
            /// </summary>
            public static readonly TimeSpan PlayOnlineStartup = TimeSpan.FromSeconds(30);

            /// <summary>
            /// Extended timeout for member selection screen detection.
            /// Default: 60 seconds - POL member screen can take longer on slower systems.
            /// Used by: ExecuteMemberSelectionAsync with ScreenDetectionOptions.Extended
            /// </summary>
            public static readonly TimeSpan MemberSelectionDetection = TimeSpan.FromSeconds(60);

            /// <summary>
            /// Standard timeout for login screen detection operations.
            /// Default: 15 seconds - typical time for login screens to appear.
            /// Used by: Login information, connection, and keyboard screens
            /// </summary>
            public static readonly TimeSpan LoginScreenDetection = TimeSpan.FromSeconds(15);

            /// <summary>
            /// Timeout for virtual keyboard appearance.
            /// Default: 10 seconds - keyboard should appear quickly after field selection.
            /// Used by: Virtual keyboard password input screen detection
            /// </summary>
            public static readonly TimeSpan VirtualKeyboardDetection = TimeSpan.FromSeconds(10);

            /// <summary>
            /// Timeout for PlayOnline main screen detection after authentication.
            /// Default: 20 seconds - allows for authentication processing and main screen load.
            /// Used by: NavigateToFinalFantasyXI main screen detection
            /// </summary>
            public static readonly TimeSpan MainScreenDetection = TimeSpan.FromSeconds(20);

            /// <summary>
            /// Timeout for PlayOnline play screens during navigation.
            /// Default: 15 seconds - game selection and play confirmation screens.
            /// Used by: Play screen and play confirmation detection
            /// </summary>
            public static readonly TimeSpan PlayScreenDetection = TimeSpan.FromSeconds(15);

            /// <summary>
            /// Maximum consecutive screenshot failures before window handle re-detection.
            /// Default: 5 attempts - balance between responsiveness and stability.
            /// Used by: WaitForScreenDetectionWithRedetectionAsync
            /// </summary>
            public const int MaxConsecutiveScreenshotFailures = 5;
        }

        /// <summary>
        /// Delay values for UI interaction and system response timing.
        /// These delays ensure proper synchronization with PlayOnline's UI responses.
        /// </summary>
        public static class Delays
        {
            /// <summary>
            /// Delay after screen detection to allow UI stabilization.
            /// Default: 1000ms - ensures UI elements are fully rendered and responsive.
            /// Used by: Member selection screen stability check
            /// </summary>
            public static readonly TimeSpan UIStabilization = TimeSpan.FromMilliseconds(1000);

            /// <summary>
            /// Extended delay for PlayOnline response after member selection.
            /// Default: 1500ms - POL needs time to process member selection.
            /// Used by: After member slot click, after login button click
            /// </summary>
            public static readonly TimeSpan PlayOnlineResponse = TimeSpan.FromMilliseconds(1500);

            /// <summary>
            /// Standard delay for UI interaction completion.
            /// Default: 1000ms - allows UI actions to complete before next step.
            /// Used by: After password field click, after confirmation actions
            /// </summary>
            public static readonly TimeSpan InteractionCompletion = TimeSpan.FromMilliseconds(1000);

            /// <summary>
            /// Brief delay for keyboard input completion.
            /// Default: 500ms - allows typed text to register properly.
            /// Used by: After password entry, after OTP entry
            /// </summary>
            public static readonly TimeSpan KeyboardInput = TimeSpan.FromMilliseconds(500);

            /// <summary>
            /// Extended delay for connection processing.
            /// Default: 2000ms - allows connection attempts to complete.
            /// Used by: After connect button clicks, after play button clicks
            /// </summary>
            public static readonly TimeSpan ConnectionProcessing = TimeSpan.FromMilliseconds(2000);

            /// <summary>
            /// Extended delay for POL Proxy transition.
            /// Default: 3000ms - allows POL Proxy to handle transition to FFXI.
            /// Used by: When POL Proxy is detected, transition timing
            /// </summary>
            public static readonly TimeSpan POLProxyTransition = TimeSpan.FromMilliseconds(3000);
        }

        /// <summary>
        /// Screen coordinates for PlayOnline UI interactions.
        /// These coordinates are relative to the PlayOnline window and are used
        /// for clicking buttons, fields, and other UI elements.
        /// </summary>
        // Coordinates removed: legacy absolute pixel positions have been eliminated in favor of template Hybrid navigation.

        /// <summary>
        /// Navigation counts for keyboard-based movement between fields on PlayOnline screens.
        /// These values may vary slightly by environment; adjust as needed.
        /// </summary>
        public static class NavigationCounts
        {
            /// <summary>
            /// Number of Tab presses to move focus from the password input to the OTP field
            /// on the Connect to PlayOnline screen. Default is 1; adjust if Tab order differs.
            /// </summary>
            public const int TabsToOTPFromPassword = 1;
        }

        /// <summary>
        /// Process names and patterns used for application detection.
        /// Centralized to ensure consistency across different detection methods.
        /// </summary>
        public static class ProcessNames
        {
            /// <summary>
            /// PlayOnline process name - primary process for POL operations.
            /// Standard process name across different POL versions.
            /// </summary>
            public const string PlayOnline = "pol";

            /// <summary>
            /// FFXI main process name - used for game detection after launch.
            /// Primary FFXI executable process name.
            /// </summary>
            public const string FFXIMain = "ffximain";

            /// <summary>
            /// Process name variations for comprehensive detection.
            /// Includes common variations and case differences.
            /// </summary>
            public static readonly string[] PlayOnlineVariations = {
                "pol",
                "POL",
                "playonline",
                "PlayOnline"
            };
        }

        /// <summary>
        /// Template paths used for screen detection operations.
        /// Centralized to ensure consistent template references and easier maintenance.
        /// </summary>
        public static class TemplatePaths
        {
            /// <summary>Base path for all PlayOnline templates</summary>
            public const string BasePath = "PlayOnline";

            /// <summary>Member selection screen template path</summary>
            public const string MemberSelectionScreen = "PlayOnline/member_selection_screen";
            /// <summary>Login information screen template path</summary>
            public const string LoginInformationScreen = "PlayOnline/login_information_screen";
            /// <summary>Connect to PlayOnline screen template path</summary>
            public const string ConnectToPlayOnlineScreen = "PlayOnline/connect_to_playonline_screen";
            /// <summary>Virtual keyboard password input screen template path</summary>
            public const string VirtualKeyboardPasswordInputScreen = "PlayOnline/virtual_keyboard_password_input_screen";
            /// <summary>Circle confirmation for password template path</summary>
            public const string CircleConfirmationPassword = "PlayOnline/circle_confirmation_password";
            /// <summary>Connect button template path</summary>
            public const string ConnectButton = "PlayOnline/connect_button";
            /// <summary>PlayOnline main screen template path</summary>
            public const string MainScreen = "PlayOnline/main_screen";
            /// <summary>Play screen template path</summary>
            public const string PlayScreen = "PlayOnline/play_screen";
            /// <summary>Play confirmation screen template path</summary>
            public const string PlayConfirmation = "PlayOnline/play_confirmation";
        }

        /// <summary>
        /// Confidence thresholds for template matching operations.
        /// These values determine the minimum confidence required for successful detection.
        /// </summary>
        public static class ConfidenceThresholds
        {
            /// <summary>
            /// Standard confidence threshold for screen detection.
            /// Default: 0.80 (80%) - ensures reliable screen detection while allowing for minor variations.
            /// Used by: Most screen detection operations throughout the handler
            /// </summary>
            public const float ScreenDetection = 0.80f;

            /// <summary>
            /// High confidence threshold for critical operations.
            /// Default: 0.90 (90%) - used for operations requiring high certainty.
            /// Reserved for: Critical authentication steps or precise UI element detection
            /// </summary>
            public const float HighConfidence = 0.90f;

            /// <summary>
            /// Low confidence threshold for diagnostic purposes.
            /// Default: 0.60 (60%) - used for detecting partial matches during debugging.
            /// Used by: Diagnostic logging to identify when elements are partially visible
            /// </summary>
            public const float DiagnosticThreshold = 0.60f;

            /// <summary>
            /// Very low confidence threshold for identifying potential screen state issues.
            /// Default: 0.10 (10%) - used for detecting complete template mismatches.
            /// Used by: Error diagnostics to identify possible template or screen state problems
            /// </summary>
            public const float VeryLowThreshold = 0.10f;
        }

        /// <summary>
        /// Progress reporting milestones for consistent user feedback.
        /// These values ensure uniform progress reporting across operations and provide
        /// meaningful feedback to users during the authentication flow.
        /// </summary>
        public static class ProgressMilestones
        {
            /// <summary>Member selection operation progress milestones</summary>
            public static class MemberSelection
            {
                public const int WindowDetection = 5;
                public const int StartupCompletion = 15;
                public const int ScreenDetection = 30;
                public const int ScreenStabilization = 50;
                public const int PreparingSelection = 60;
                public const int ClickingMember = 70;
                public const int ConfirmingSelection = 85;
                public const int Complete = 100;
            }

            /// <summary>Password entry operation progress milestones</summary>
            public static class PasswordEntry
            {
                public const int WindowDetection = 5;
                public const int LoginScreenDetection = 15;
                public const int LoginButtonClick = 25;
                public const int ConnectionScreenDetection = 35;
                public const int PasswordFieldClick = 45;
                public const int VirtualKeyboardDetection = 55;
                public const int KeyboardFieldClick = 65;
                public const int PasswordRetrieval = 75;
                public const int PasswordInput = 85;
                public const int Confirmation = 95;
                public const int Complete = 100;
            }

            /// <summary>OTP entry operation progress milestones</summary>
            public static class OTPEntry
            {
                public const int OTPGeneration = 10;
                public const int FieldLocation = 30;
                public const int OTPInput = 60;
                public const int Connection = 80;
                public const int PostProcessing = 90;
                public const int Complete = 100;
            }

            /// <summary>Navigation operation progress milestones</summary>
            public static class Navigation
            {
                public const int MainScreenWait = 75;
                public const int GameSelection = 80;
                public const int PlayScreenWait = 85;
                public const int FinalConfirmation = 90;
                public const int Complete = 95;
            }
        }

        /// <summary>
        /// Member slot configuration and validation settings.
        /// </summary>
        public static class MemberSlots
        {
            /// <summary>Minimum valid member slot number</summary>
            public const int MinSlot = 1;
            /// <summary>Maximum valid member slot number</summary>
            public const int MaxSlot = 4;

            /// <summary>
            /// Validates that a member slot number is within the valid range.
            /// </summary>
            /// <param name="slotNumber">Member slot number to validate</param>
            /// <returns>True if slot number is valid (1-4), false otherwise</returns>
            public static bool IsValidSlot(int slotNumber)
            {
                return slotNumber >= MinSlot && slotNumber <= MaxSlot;
            }

            /// <summary>
            /// Validates member slot number and throws exception if invalid.
            /// </summary>
            /// <param name="slotNumber">Member slot number to validate</param>
            /// <exception cref="ArgumentException">Thrown if slot number is not between 1 and 4</exception>
            public static void ValidateSlot(int slotNumber)
            {
                if (!IsValidSlot(slotNumber))
                {
                    throw new ArgumentException($"Invalid member slot: {slotNumber}. Must be between {MinSlot} and {MaxSlot}.", nameof(slotNumber));
                }
            }
        }

        /// <summary>
        /// Window title patterns for PlayOnline and FFXI window detection.
        /// Used by window handle detection and re-detection logic.
        /// </summary>
        public static class WindowTitles
        {
            /// <summary>
            /// Window title patterns that indicate a PlayOnline window.
            /// Used for window handle detection and validation.
            /// </summary>
            public static readonly string[] PlayOnlinePatterns = {
                "PlayOnline",
                "PLAYONLINE"
            };

            /// <summary>
            /// Window title patterns that indicate an FFXI window.
            /// Used for detecting transition from PlayOnline to FFXI.
            /// </summary>
            public static readonly string[] FFXIPatterns = {
                "FINAL FANTASY",
                "FFXI"
            };

            /// <summary>
            /// Combined patterns for detecting either PlayOnline or FFXI windows.
            /// Useful for window handle re-detection during transitions.
            /// </summary>
            public static readonly string[] CombinedPatterns = {
                "PlayOnline",
                "PLAYONLINE", 
                "FINAL FANTASY",
                "FFXI"
            };
        }

        /// <summary>
        /// Application detection patterns for external applications.
        /// Used to identify POL Proxy and other related applications.
        /// </summary>
        public static class ApplicationPatterns
        {
            /// <summary>
            /// Name patterns that indicate a POL Proxy application.
            /// Used for detecting if POL Proxy is configured and should modify flow.
            /// </summary>
            public static readonly string[] POLProxyNamePatterns = {
                "POL Proxy",
                "POLProxy",
                "pol proxy"
            };

            /// <summary>
            /// Path patterns that indicate a POL Proxy application.
            /// Used as fallback when name matching fails.
            /// </summary>
            public static readonly string[] POLProxyPathPatterns = {
                "POLProxy",
                "pol-proxy",
                "polproxy"
            };
        }
    }
}
