using System;

namespace FFXIManager.Models
{
    /// <summary>
    /// DEPRECATED: Legacy enum for backward compatibility only.
    /// Auto-login is now 100% workflow-driven via WorkflowStepDefinition.
    /// See workflows/defaults/playonline-standard.json for the complete login sequence.
    /// </summary>
    [System.Obsolete("Use WorkflowStepDefinition instead. This enum is kept only for backward compatibility with AutoLoginSubtask.TaskStep property.")]
    public enum LoginTaskStep
    {
        /// <summary>
        /// No step currently active (default value)
        /// </summary>
        None = 0
    }

    /// <summary>
    /// DEPRECATED: Extension methods for legacy LoginTaskStep enum
    /// </summary>
    [System.Obsolete("Use WorkflowStepDefinition properties instead.")]
    public static class LoginTaskStepExtensions
    {
        /// <summary>
        /// Gets the display name for a login task step
        /// </summary>
        public static string GetDisplayName(this LoginTaskStep step) => "None";

        /// <summary>
        /// Gets the short display name for a login task step
        /// </summary>
        public static string GetShortDisplayName(this LoginTaskStep step) => "None";

        /// <summary>
        /// Gets the estimated duration in seconds for this step
        /// </summary>
        public static int GetEstimatedDurationSeconds(this LoginTaskStep step) => 5;

        /// <summary>
        /// DEPRECATED: Returns empty list. Use workflow system instead.
        /// </summary>
        public static IEnumerable<LoginTaskStep> GetMainSteps()
        {
            return Array.Empty<LoginTaskStep>();
        }

        /// <summary>
        /// DEPRECATED: Returns empty list. Use workflow system instead.
        /// </summary>
        public static IEnumerable<LoginTaskStep> GetPOLProxySubTasks()
        {
            return Array.Empty<LoginTaskStep>();
        }

        /// <summary>
        /// DEPRECATED: Returns empty list. Use workflow system instead.
        /// </summary>
        public static IEnumerable<LoginTaskStep> GetWindowerSubTasks()
        {
            return Array.Empty<LoginTaskStep>();
        }
    }
}