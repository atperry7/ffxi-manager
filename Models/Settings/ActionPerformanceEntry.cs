namespace FFXIManager.Models.Settings
{
    /// <summary>
    /// Action-level performance metrics for granular workflow optimization.
    /// Tracks performance of individual workflow actions (Click, Keyboard, InputPassword, Launch, etc.)
    /// </summary>
    public class ActionPerformanceEntry
    {
        /// <summary>
        /// Action type identifier (e.g., "Click", "Keyboard", "InputPassword", "Launch", "Wait")
        /// </summary>
        public string ActionType { get; set; } = string.Empty;

        /// <summary>
        /// Total number of times this action was executed
        /// </summary>
        public int Executions { get; set; }

        /// <summary>
        /// Number of successful executions
        /// </summary>
        public int Successes { get; set; }

        /// <summary>
        /// Number of failed executions
        /// </summary>
        public int Failures { get; set; }

        /// <summary>
        /// Total execution time across all executions
        /// </summary>
        public System.TimeSpan TotalDuration { get; set; }

        /// <summary>
        /// Average execution time per action
        /// </summary>
        public System.TimeSpan AverageDuration { get; set; }

        /// <summary>
        /// Total number of retry attempts across all executions
        /// </summary>
        public int TotalRetries { get; set; }

        /// <summary>
        /// Average number of retries per execution
        /// </summary>
        public double AverageRetries { get; set; }

        /// <summary>
        /// Number of times action succeeded on first attempt (no retries)
        /// </summary>
        public int FirstAttemptSuccesses { get; set; }

        /// <summary>
        /// Minimum execution time observed
        /// </summary>
        public System.TimeSpan MinDuration { get; set; } = System.TimeSpan.MaxValue;

        /// <summary>
        /// Maximum execution time observed
        /// </summary>
        public System.TimeSpan MaxDuration { get; set; }

        /// <summary>
        /// Success rate (0.0 to 1.0)
        /// </summary>
        public double SuccessRate => Executions > 0 ? (double)Successes / Executions : 0.0;
    }
}
