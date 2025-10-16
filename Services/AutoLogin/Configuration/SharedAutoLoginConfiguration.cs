namespace FFXIManager.Services.AutoLogin.Configuration
{
    /// <summary>
    /// Shared configuration constants for auto-login operations.
    /// Contains only the minimal configuration needed by process launch handlers and diagnostic tools.
    /// Most auto-login behavior is now defined in data-driven workflow JSON files.
    /// </summary>
    public static class SharedAutoLoginConfiguration
    {
        /// <summary>
        /// Process names for PlayOnline and FFXI detection.
        /// </summary>
        public static class ProcessNames
        {
            /// <summary>PlayOnline process name</summary>
            public const string PlayOnline = "pol";

            /// <summary>FFXI main process name</summary>
            public const string FFXIMain = "ffximain";
        }

        /// <summary>
        /// Application detection patterns for external applications.
        /// </summary>
        public static class ApplicationPatterns
        {
            /// <summary>
            /// Name patterns that indicate a POL Proxy application.
            /// Used for detecting if POL Proxy is configured.
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
