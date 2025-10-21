namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Provides type-safe, standardized context keys for auto-login workflow execution.
    /// Eliminates magic strings and provides consistent naming conventions for context data.
    /// </summary>
    /// <remarks>
    /// **Architecture: PID-First Design**
    /// - Process IDs (PIDs) are the source of truth for launched applications
    /// - Window handles are derived/cached values that can be refreshed from PIDs
    /// - PIDs remain stable for process lifetime; window handles can become stale
    ///
    /// **Key Patterns:**
    /// - Launch steps store PID: `launch_{stepId}_ProcessId`
    /// - Launch metadata: `launch_{stepId}_Skipped`, `launch_{stepId}_ApplicationName`
    /// - Detection results: `detect_{stepId}_TemplateMatch`
    /// - Navigation state: `nav_{stepId}_LastAction`
    /// </remarks>
    public static class AutoLoginContextKeys
    {
        /// <summary>
        /// Gets context key for a launched application's process ID.
        /// PID is the source of truth - always store this when launching applications.
        /// </summary>
        /// <param name="stepId">Workflow step identifier</param>
        /// <returns>Context key for process ID storage</returns>
        public static string LaunchProcessId(string stepId) => $"launch_{NormalizeStepId(stepId)}_ProcessId";

        /// <summary>
        /// Gets context key for launch skip flag (indicates if launch was skipped because app already running).
        /// </summary>
        public static string LaunchSkipped(string stepId) => $"launch_{NormalizeStepId(stepId)}_Skipped";

        /// <summary>
        /// Gets context key for the application name that was launched.
        /// </summary>
        public static string LaunchApplicationName(string stepId) => $"launch_{NormalizeStepId(stepId)}_ApplicationName";

        /// <summary>
        /// Gets context key for launch timestamp (when process was started).
        /// </summary>
        public static string LaunchTimestamp(string stepId) => $"launch_{NormalizeStepId(stepId)}_Timestamp";

        /// <summary>
        /// Gets context key for a launched application's process ID using application GUID.
        /// Preferred over name/step-based keys to avoid rename fragility.
        /// </summary>
        public static string LaunchProcessIdByApp(Guid appId) => $"launch_app_{appId:N}_ProcessId";

        /// <summary>
        /// Gets context key for launch timestamp using application GUID.
        /// </summary>
        public static string LaunchTimestampByApp(Guid appId) => $"launch_app_{appId:N}_Timestamp";

        /// <summary>
        /// Stores an association of application name to GUID so later steps can resolve Id from name.
        /// </summary>
        public static string ApplicationIdMap(string applicationName) => $"app_{NormalizeStepId(applicationName)}_Id";

        /// <summary>
        /// Gets context key for cached window handle (refreshable from PID).
        /// Note: Window handles can become stale - always re-validate or refresh from PID before use.
        /// </summary>
        public static string CachedWindowHandle(string stepId) => $"cache_{NormalizeStepId(stepId)}_WindowHandle";

        /// <summary>
        /// Gets context key for window handle last refresh timestamp.
        /// Used to determine if cached window handle needs refreshing.
        /// </summary>
        public static string WindowHandleRefreshTime(string stepId) => $"cache_{NormalizeStepId(stepId)}_RefreshTime";

        /// <summary>
        /// Gets context key for template match result from detection.
        /// </summary>
        public static string DetectionResult(string stepId) => $"detect_{NormalizeStepId(stepId)}_TemplateMatch";

        /// <summary>
        /// Gets context key for detection confidence score.
        /// </summary>
        public static string DetectionConfidence(string stepId) => $"detect_{NormalizeStepId(stepId)}_Confidence";

        /// <summary>
        /// Gets context key for last navigation action performed in a step.
        /// </summary>
        public static string LastNavigationAction(string stepId) => $"nav_{NormalizeStepId(stepId)}_LastAction";

        /// <summary>
        /// Gets context key for navigation retry attempt count.
        /// </summary>
        public static string NavigationRetryCount(string stepId) => $"nav_{NormalizeStepId(stepId)}_RetryCount";

        /// <summary>
        /// Gets context key for error message from failed step.
        /// </summary>
        public static string StepError(string stepId) => $"error_{NormalizeStepId(stepId)}_Message";

        /// <summary>
        /// Gets context key for error exception type from failed step.
        /// </summary>
        public static string StepErrorType(string stepId) => $"error_{NormalizeStepId(stepId)}_Type";

        /// <summary>
        /// Normalizes step ID to create consistent context keys.
        /// Converts to lowercase and replaces spaces/special chars with underscores.
        /// </summary>
        private static string NormalizeStepId(string stepId)
        {
            if (string.IsNullOrWhiteSpace(stepId))
                return "unknown";

            return stepId
                .ToLowerInvariant()
                .Replace(" ", "_")
                .Replace("-", "_")
                .Replace(".", "_");
        }

        /// <summary>
        /// Well-known context keys for common launch scenarios.
        /// These cover the standard PlayOnline/FFXI launch sequence.
        /// </summary>
        public static class WellKnown
        {
            /// <summary>Windower process ID</summary>
            public const string WindowerProcessId = "launch_windower_ProcessId";

            /// <summary>POL Proxy process ID</summary>
            public const string POLProxyProcessId = "launch_pol_proxy_ProcessId";

            /// <summary>PlayOnline Viewer process ID</summary>
            public const string PlayOnlineProcessId = "launch_playonline_ProcessId";

            /// <summary>FFXI process ID</summary>
            public const string FFXIProcessId = "launch_ffxi_ProcessId";

            /// <summary>Ashita process ID</summary>
            public const string AshitaProcessId = "launch_ashita_ProcessId";
        }
    }
}
