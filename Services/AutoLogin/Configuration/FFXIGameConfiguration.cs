using System;

namespace FFXIManager.Services.AutoLogin.Configuration
{
    /// <summary>
    /// Configuration constants for Final Fantasy XI game handler operations.
    /// Centralizes timeout values, process names, delays, and other configuration settings
    /// to improve maintainability and eliminate magic numbers throughout the FFXI handler.
    /// 
    /// This configuration follows the established pattern set by WindowerLaunchConfiguration
    /// and PlayOnlineAuthConfiguration, providing centralized management of all hardcoded values
    /// previously scattered throughout the FFXIGameHandler implementation.
    /// </summary>
    /// <remarks>
    /// **Extracted Constants Summary:**
    /// - Screen detection timeouts: 90s terms, 60s menu, 45s character selection
    /// - UI interaction delays: 500ms focus, 1000ms stabilization, 2000ms transition
    /// - Process detection configuration: process names, window title patterns
    /// - Navigation delays: 750ms preparation, 1000ms between steps
    /// - Loading timeouts: 3000ms character, 5000ms game world
    /// - Retry configuration: 5 consecutive failures, 60 detection attempts
    /// - Progress milestones: Consistent progress reporting values
    /// </remarks>
    public static class FFXIGameConfiguration
    {
        /// <summary>
        /// Timeout values for various FFXI screen detection and waiting operations.
        /// These values are based on typical FFXI loading times and can be
        /// adjusted based on system performance characteristics.
        /// </summary>
        public static class Timeouts
        {
            /// <summary>
            /// Maximum time to wait for FFXI Terms of Service screen to appear.
            /// Default: 90 seconds - Extended timeout to account for FFXI's slow initial loading,
            /// including DirectX initialization and asset loading on first startup.
            /// </summary>
            public static readonly TimeSpan TermsAcceptance = TimeSpan.FromSeconds(90);

            /// <summary>
            /// Maximum time to wait for FFXI main menu screen to appear after terms acceptance.
            /// Default: 60 seconds - Allows for menu initialization and background loading.
            /// </summary>
            public static readonly TimeSpan MainMenu = TimeSpan.FromSeconds(60);

            /// <summary>
            /// Maximum time to wait for character slot selection screen to appear.
            /// Default: 45 seconds - Character list loading including server communication.
            /// </summary>
            public static readonly TimeSpan CharacterSlot = TimeSpan.FromSeconds(45);

            /// <summary>
            /// Maximum time to wait for character confirmation screen to appear.
            /// Default: 45 seconds - Character data loading and preparation for world entry.
            /// </summary>
            public static readonly TimeSpan CharacterConfirmation = TimeSpan.FromSeconds(45);

            /// <summary>
            /// Maximum time to wait for FFXI process to launch and become detectable.
            /// Default: 60 seconds - Includes process startup, DirectX initialization, and window creation.
            /// </summary>
            public static readonly TimeSpan ProcessDetection = TimeSpan.FromSeconds(60);
        }

        /// <summary>
        /// Delay values for UI interaction timing and synchronization.
        /// These delays ensure proper timing between operations and allow
        /// FFXI's DirectX interface to stabilize between interactions.
        /// </summary>
        public static class Delays
        {
            /// <summary>
            /// Delay after ensuring window focus before performing UI interactions.
            /// Default: 500ms - Allows window focus to complete and become responsive.
            /// Essential for DirectX applications that may not immediately respond to input.
            /// </summary>
            public static readonly TimeSpan WindowFocus = TimeSpan.FromMilliseconds(500);

            /// <summary>
            /// Delay to allow screen transitions and UI elements to stabilize.
            /// Default: 1000ms - Ensures UI elements are fully rendered and interactive.
            /// Critical for FFXI's DirectX rendering which may lag behind logical state changes.
            /// </summary>
            public static readonly TimeSpan ScreenStabilization = TimeSpan.FromMilliseconds(1000);

            /// <summary>
            /// Delay after major screen transitions (e.g., terms accepted, menu selected).
            /// Default: 2000ms - Allows for screen transitions, animations, and loading.
            /// Accounts for FFXI's transition animations and background processing.
            /// </summary>
            public static readonly TimeSpan Transition = TimeSpan.FromMilliseconds(2000);

            /// <summary>
            /// Delay between individual navigation steps (arrow key presses).
            /// Default: 1000ms - Ensures FFXI processes each navigation input fully.
            /// Prevents input queue overflow and ensures UI state synchronization.
            /// </summary>
            public static readonly TimeSpan NavigationStep = TimeSpan.FromMilliseconds(1000);

            /// <summary>
            /// Extended delay for character loading operations.
            /// Default: 3000ms - Character selection, slot confirmation, initial loading.
            /// Allows for character asset loading and server communication.
            /// </summary>
            public static readonly TimeSpan CharacterLoading = TimeSpan.FromMilliseconds(3000);

            /// <summary>
            /// Extended delay for game world entry and loading.
            /// Default: 5000ms - World loading, zone initialization, character placement.
            /// Accounts for the significant time required for full game world loading.
            /// </summary>
            public static readonly TimeSpan GameWorldLoading = TimeSpan.FromMilliseconds(5000);

            /// <summary>
            /// Delay for focus stabilization in complex navigation scenarios.
            /// Default: 750ms - Used for critical navigation where longer focus time is needed.
            /// Particularly important for character slot navigation and selection.
            /// </summary>
            public static readonly TimeSpan FocusStabilization = TimeSpan.FromMilliseconds(750);

            /// <summary>
            /// Delay before sending Enter key for important confirmations.
            /// Default: 750ms - Ensures window is ready to receive confirmation input.
            /// Used for critical operations like character selection and login confirmation.
            /// </summary>
            public static readonly TimeSpan EnterPreparation = TimeSpan.FromMilliseconds(750);
        }

        /// <summary>
        /// Screen detection intervals and polling frequencies.
        /// These intervals balance detection responsiveness with system resource usage.
        /// </summary>
        public static class PollingIntervals
        {
            /// <summary>
            /// Interval between screen detection attempts for complex screens.
            /// Default: 2000ms - Used for screens with longer loading times (terms, game world).
            /// Reduces polling frequency for operations that are inherently slow.
            /// </summary>
            public static readonly TimeSpan ScreenCheck = TimeSpan.FromSeconds(2);

            /// <summary>
            /// Interval between screen detection attempts for menu screens.
            /// Default: 1500ms - Used for menu navigation and character selection screens.
            /// Balanced frequency for moderately responsive UI elements.
            /// </summary>
            public static readonly TimeSpan MenuCheck = TimeSpan.FromSeconds(1.5);

            /// <summary>
            /// Interval between process detection attempts during startup.
            /// Default: 1000ms - Balances quick process detection with resource usage.
            /// Standard frequency for process monitoring operations.
            /// </summary>
            public static readonly TimeSpan ProcessCheck = TimeSpan.FromSeconds(1);
        }

        /// <summary>
        /// Process identification and window detection configuration.
        /// Centralized definitions for FFXI process discovery and window management.
        /// </summary>
        public static class ProcessDiscovery
        {
            /// <summary>
            /// Process names that may represent FFXI game processes.
            /// Includes both PlayOnline launcher and FFXI main executable names.
            /// Used for process discovery and window handle detection.
            /// </summary>
            public static readonly string[] ProcessNames = { "pol", "ffximain" };

            /// <summary>
            /// Window title patterns that indicate FFXI game windows.
            /// Used for window discovery when process names are insufficient.
            /// Case-insensitive matching is used for these patterns.
            /// </summary>
            public static readonly string[] WindowTitlePatterns = { "FINAL FANTASY", "FFXI" };

            /// <summary>
            /// Maximum number of consecutive screenshot failures before attempting window redetection.
            /// Default: 5 - Balances retry attempts with responsiveness to window handle changes.
            /// Prevents excessive retries while allowing for temporary screenshot failures.
            /// </summary>
            public const int MaxConsecutiveFailures = 5;

            /// <summary>
            /// Maximum number of attempts to detect FFXI process during startup.
            /// Default: 60 - Provides 60 seconds of process detection attempts (1 attempt per second).
            /// Accommodates slow system startups and various FFXI launch configurations.
            /// </summary>
            public const int ProcessDetectionAttempts = 60;

            /// <summary>
            /// Maximum number of attempts to verify FFXI window responsiveness.
            /// Default: 10 - Provides 10 seconds of responsiveness checking.
            /// Ensures window is stable and ready for interaction before proceeding.
            /// </summary>
            public const int ResponsivenessCheckAttempts = 10;
        }

        /// <summary>
        /// Template paths used for FFXI screen detection operations.
        /// Centralized template references to ensure consistency across the handler.
        /// </summary>
        public static class TemplatePaths
        {
            /// <summary>
            /// Template path for FFXI Terms of Service acceptance screen.
            /// Used for detecting when the terms screen is displayed and ready for interaction.
            /// </summary>
            public const string TermsAcceptance = "FFXI/ffxi_accept_terms";

            /// <summary>
            /// Template path for FFXI main menu screen.
            /// Used for detecting when the main game menu is available for navigation.
            /// </summary>
            public const string MainMenu = "FFXI/ffxi_main_screen";

            /// <summary>
            /// Template path for character slot selection screen.
            /// Used for detecting when character slots are displayed and selectable.
            /// </summary>
            public const string CharacterSlotSelection = "FFXI/ffxi_character_slot_select_screen";

            /// <summary>
            /// Template path for character confirmation screen.
            /// Used for detecting when character is loaded and ready for final confirmation.
            /// </summary>
            public const string CharacterConfirmation = "FFXI/ffxi_character_confirmation";
        }

        /// <summary>
        /// Progress reporting milestones for consistent user feedback.
        /// These values ensure uniform progress reporting across different FFXI operations.
        /// </summary>
        public static class ProgressMilestones
        {
            // Terms Acceptance Progress
            public const int TermsProcessWait = 5;
            public const int TermsScreenDetection = 15;
            public const int TermsScreenDetected = 60;
            public const int TermsAccepting = 70;
            public const int TermsComplete = 100;

            // Main Menu Progress
            public const int MenuWait = 10;
            public const int MenuDetected = 50;
            public const int MenuNavigating = 70;
            public const int MenuComplete = 100;

            // Character Slot Selection Progress
            public const int SlotWait = 10;
            public const int SlotDetected = 40;
            public const int SlotNavigationStart = 50;
            public const int SlotNavigationEnd = 70;
            public const int SlotSelecting = 80;
            public const int SlotComplete = 100;

            // Character Confirmation Progress
            public const int ConfirmationWait = 10;
            public const int ConfirmationDetected = 40;
            public const int ConfirmationConfirming = 60;
            public const int ConfirmationSent = 80;
            public const int ConfirmationVerifying = 95;
            public const int ConfirmationComplete = 100;

            // Process Detection Progress
            public const int ProcessSearching = 5;
            public const int ProcessFound = 12;
            public const int ProcessResponsive = 15;

            // Screen Detection Progress
            public const int DetectionAttempting = 95;
            public const int DetectionSuccessful = 100;
        }

        /// <summary>
        /// Character slot validation and navigation configuration.
        /// Defines valid character slot ranges and navigation parameters.
        /// </summary>
        public static class CharacterSlots
        {
            /// <summary>
            /// Minimum valid character slot number.
            /// FFXI character slots are numbered starting from 1.
            /// </summary>
            public const int MinSlotNumber = 1;

            /// <summary>
            /// Maximum valid character slot number.
            /// FFXI supports up to 16 character slots per account.
            /// </summary>
            public const int MaxSlotNumber = 16;

            /// <summary>
            /// Default character slot when none is specified.
            /// Uses slot 1 as the default for backwards compatibility.
            /// </summary>
            public const int DefaultSlotNumber = 1;
        }
    }
}