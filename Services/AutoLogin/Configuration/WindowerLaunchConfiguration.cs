using System;

namespace FFXIManager.Services.AutoLogin.Configuration
{
    /// <summary>
    /// Configuration constants for Windower application launch operations.
    /// Centralizes timeout values, process names, and other configuration settings
    /// to improve maintainability and eliminate magic numbers throughout the handler.
    /// </summary>
    public static class WindowerLaunchConfiguration
    {
        /// <summary>
        /// Timeout values for various Windower launch operations.
        /// These values are based on typical application startup times and can be
        /// adjusted based on system performance characteristics.
        /// </summary>
        public static class Timeouts
        {
            /// <summary>
            /// Maximum time to wait for Windower process to start after launch command.
            /// Default: 10 seconds - allows for typical application startup on most systems.
            /// </summary>
            public static readonly TimeSpan ProcessStartup = TimeSpan.FromSeconds(10);

            /// <summary>
            /// Maximum time to wait for Windower process to become responsive.
            /// Default: 15 seconds - accounts for initialization and loading time.
            /// </summary>
            public static readonly TimeSpan ProcessResponsiveness = TimeSpan.FromSeconds(15);

            /// <summary>
            /// Maximum time to wait for Windower main window to appear and be accessible.
            /// Default: 10 seconds - typical time for UI initialization.
            /// </summary>
            public static readonly TimeSpan WindowDetection = TimeSpan.FromSeconds(10);

            /// <summary>
            /// Time to wait for PlayOnline process to start after clicking launch button.
            /// Default: 10 seconds - allows for POL initialization through Windower.
            /// </summary>
            public static readonly TimeSpan PlayOnlineStartup = TimeSpan.FromSeconds(10);

            /// <summary>
            /// Delay to allow UI stabilization before performing interactions.
            /// Default: 2000ms - ensures UI elements are fully rendered and responsive.
            /// </summary>
            public static readonly TimeSpan UIStabilization = TimeSpan.FromMilliseconds(2000);

            /// <summary>
            /// Brief delay between window focus and interaction operations.
            /// Default: 500ms - allows window focus to complete before UI interaction.
            /// </summary>
            public static readonly TimeSpan WindowFocusDelay = TimeSpan.FromMilliseconds(500);

            /// <summary>
            /// Wait time after clicking launch button before checking for POL process.
            /// Default: 2000ms - allows launch command to initiate POL startup.
            /// </summary>
            public static readonly TimeSpan PostLaunchDelay = TimeSpan.FromMilliseconds(2000);
        }

        /// <summary>
        /// Polling intervals for various monitoring operations.
        /// These intervals balance responsiveness with system resource usage.
        /// </summary>
        public static class PollingIntervals
        {
            /// <summary>
            /// Interval between process startup checks.
            /// Default: 500ms - provides good responsiveness without excessive polling.
            /// </summary>
            public static readonly TimeSpan ProcessStartupCheck = TimeSpan.FromMilliseconds(500);

            /// <summary>
            /// Interval between window detection attempts.
            /// Default: 500ms - balances detection speed with resource usage.
            /// </summary>
            public static readonly TimeSpan WindowDetectionCheck = TimeSpan.FromMilliseconds(500);

            /// <summary>
            /// Interval between PlayOnline process detection attempts.
            /// Default: 500ms - provides timely detection without excessive polling.
            /// </summary>
            public static readonly TimeSpan PlayOnlineDetectionCheck = TimeSpan.FromMilliseconds(500);
        }

        /// <summary>
        /// Process names and patterns used for application detection.
        /// Centralized to ensure consistency across different detection methods.
        /// </summary>
        public static class ProcessNames
        {
            /// <summary>
            /// Primary process name for Windower executable.
            /// This is the most common process name used by Windower.
            /// </summary>
            public const string Windower = "windower";

            /// <summary>
            /// Alternative process names that might be used by different Windower versions.
            /// Includes common variations and version-specific names.
            /// </summary>
            public static readonly string[] WindowerVariations = {
                "windower",
                "Windower",
                "windower4",
                "Windower4"
            };

            /// <summary>
            /// PlayOnline process name used for detecting successful launch.
            /// Standard POL process name across different game versions.
            /// </summary>
            public const string PlayOnline = "pol";
        }

        /// <summary>
        /// Application identification patterns for discovery operations.
        /// Used to locate Windower in the external applications configuration.
        /// </summary>
        public static class ApplicationPatterns
        {
            /// <summary>
            /// Name patterns that indicate a Windower application.
            /// Used for fuzzy matching in application discovery.
            /// </summary>
            public static readonly string[] NamePatterns = {
                "Windower",
                "windower"
            };

            /// <summary>
            /// Executable path patterns that indicate a Windower application.
            /// Used as fallback when name matching fails.
            /// </summary>
            public static readonly string[] PathPatterns = {
                "windower",
                "Windower"
            };
        }

        /// <summary>
        /// Template paths used for screen detection operations.
        /// Centralized to ensure consistent template references.
        /// </summary>
        public static class TemplatePaths
        {
            /// <summary>
            /// Template path for Windower launch arrow button detection.
            /// Used in both verification and clicking operations.
            /// </summary>
            public const string LaunchArrow = "Windower/launch_arrow";
        }

        /// <summary>
        /// Confidence thresholds for template matching operations.
        /// These values determine the minimum confidence required for successful detection.
        /// </summary>
        public static class ConfidenceThresholds
        {
            /// <summary>
            /// Minimum confidence required for launch arrow detection.
            /// Default: 0.80 (80%) - ensures reliable button detection.
            /// </summary>
            public const float LaunchArrowDetection = 0.80f;
        }

        /// <summary>
        /// Progress reporting milestones for consistent user feedback.
        /// These values ensure uniform progress reporting across operations.
        /// </summary>
        public static class ProgressMilestones
        {
            public const int ApplicationDiscovery = 10;
            public const int ConfigurationValidation = 30;
            public const int ProcessLaunch = 50;
            public const int ProcessStartupWait = 70;
            public const int WindowDetection = 90;
            public const int OperationComplete = 100;

            // Wait for startup milestones
            public const int ResponsivenessCheck = 20;
            public const int WindowAvailability = 60;
            public const int StartupComplete = 100;

            // UI verification milestones
            public const int UIInitialization = 15;
            public const int UIDetection = 100;

            // Launch button milestones
            public const int WindowFocus = 15;
            public const int ButtonDetection = 30;
            public const int ButtonClick = 75;
            public const int PostLaunchWait = 90;
        }
    }
}