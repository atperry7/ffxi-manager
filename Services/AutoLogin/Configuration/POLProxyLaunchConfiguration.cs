using System;

namespace FFXIManager.Services.AutoLogin.Configuration
{
    /// <summary>
    /// Configuration constants and settings for POL Proxy launch operations within the auto-login system.
    /// Provides centralized timeout values, process names, and progress milestones for maintainable
    /// and consistent POL Proxy launch behavior.
    /// 
    /// This configuration supports the optional POL Proxy launch step that precedes Windower launch,
    /// ensuring users don't need to manually start POL Proxy before using auto-login.
    /// </summary>
    public static class POLProxyLaunchConfiguration
    {
        /// <summary>
        /// Timeout values for POL Proxy launch operations.
        /// Balanced for responsiveness while allowing sufficient time for process startup.
        /// </summary>
        public static class Timeouts
        {
            /// <summary>Process startup detection timeout</summary>
            public static readonly TimeSpan ProcessStartup = TimeSpan.FromSeconds(10);
            
            /// <summary>Process responsiveness verification timeout</summary>
            public static readonly TimeSpan ProcessResponsiveness = TimeSpan.FromSeconds(8);
            
            /// <summary>Post-launch stabilization delay</summary>
            public static readonly TimeSpan PostLaunchDelay = TimeSpan.FromSeconds(2);
            
            /// <summary>Configuration detection timeout</summary>
            public static readonly TimeSpan ConfigurationCheck = TimeSpan.FromSeconds(1);
        }

        /// <summary>
        /// Polling intervals for various monitoring operations.
        /// Optimized for responsiveness without excessive resource usage.
        /// </summary>
        public static class PollingIntervals
        {
            /// <summary>Process startup monitoring check interval</summary>
            public static readonly TimeSpan ProcessStartupCheck = TimeSpan.FromMilliseconds(500);
            
            /// <summary>Process responsiveness check interval</summary>
            public static readonly TimeSpan ResponsivenessCheck = TimeSpan.FromMilliseconds(200);
        }

        /// <summary>
        /// Process name patterns for POL Proxy detection.
        /// Covers common POL Proxy executable variations.
        /// </summary>
        public static class ProcessNames
        {
            /// <summary>Primary POL Proxy process name</summary>
            public const string POLProxy = "POLProxy";
            
            /// <summary>Alternative POL Proxy process names to check</summary>
            public static readonly string[] POLProxyVariations = {
                "POLProxy",
                "polproxy",
                "POL Proxy",
                "pol-proxy",
                "PolProxy"
            };
        }

        /// <summary>
        /// Progress milestone constants for consistent progress reporting.
        /// Provides standardized progress values for UI consistency.
        /// </summary>
        public static class ProgressMilestones
        {
            /// <summary>Configuration detection phase</summary>
            public const int ConfigurationDetection = 10;
            
            /// <summary>Process status check phase</summary>
            public const int ProcessStatusCheck = 25;
            
            /// <summary>Application launch phase</summary>
            public const int ApplicationLaunch = 50;
            
            /// <summary>Process startup verification phase</summary>
            public const int ProcessStartupVerification = 75;
            
            /// <summary>Post-launch stabilization phase</summary>
            public const int PostLaunchStabilization = 90;
            
            /// <summary>Operation complete</summary>
            public const int OperationComplete = 100;
        }

        /// <summary>
        /// Confidence thresholds for various detection operations.
        /// Currently not used for POL Proxy (no UI detection required) but maintained for consistency.
        /// </summary>
        public static class ConfidenceThresholds
        {
            /// <summary>Minimum confidence for process detection (not currently used)</summary>
            public const float ProcessDetection = 0.80f;
        }

        /// <summary>
        /// Default settings for POL Proxy auto-launch behavior.
        /// These can be overridden by user configuration settings.
        /// </summary>
        public static class Defaults
        {
            /// <summary>Whether POL Proxy auto-launch is enabled by default</summary>
            public const bool EnableAutoLaunch = true;
            
            /// <summary>Whether to continue if POL Proxy launch fails</summary>
            public const bool ContinueOnFailure = true;
            
            /// <summary>Maximum number of launch retry attempts</summary>
            public const int MaxRetryAttempts = 1;
        }
    }
}
