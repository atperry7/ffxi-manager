using FFXIManager.Models;
using FFXIManager.Models.AutoLogin;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Resolves the target application name for workflow steps and actions.
    /// Consolidates application detection logic to prevent duplication.
    /// </summary>
    /// <remarks>
    /// **Resolution Priority:**
    /// 1. Explicit TargetApplication parameter on action
    /// 2. Most recent Launch action's ApplicationName in sequence
    /// 3. Step-level TemplatePath prefix (e.g., "Windower/template.png" → "Windower")
    /// 4. Default fallback (usually "PlayOnline")
    ///
    /// **Design Rationale:**
    /// - Single Responsibility: Knows how to find target application names
    /// - Reduces duplicate logic across DynamicWorkflowHandler
    /// - Clear, documented priority rules
    /// - Easy to test and maintain
    /// </remarks>
    public static class TargetApplicationResolver
    {
        /// <summary>
        /// Resolves the target application for a workflow step.
        /// Used for detection-only steps or initial step setup.
        /// </summary>
        /// <param name="step">Workflow step definition</param>
        /// <param name="defaultApp">Default application if resolution fails (default: "PlayOnline")</param>
        /// <returns>Target application name</returns>
        public static string ResolveForStep(WorkflowStepDefinition step, string defaultApp = "PlayOnline")
        {
            if (step == null)
                return defaultApp;

            // Priority 1: Check if step has Launch action - use that app
            var launchAction = FindLaunchAction(step.Navigation);
            if (launchAction != null)
            {
                var appName = launchAction.GetParameter<string>("ApplicationName", string.Empty);
                if (!string.IsNullOrWhiteSpace(appName))
                    return appName;
            }

            // Priority 2: Derive from step-level TemplatePath prefix
            var appFromTemplate = ExtractApplicationFromTemplatePath(step.TemplatePath);
            if (!string.IsNullOrWhiteSpace(appFromTemplate))
                return appFromTemplate;

            // Priority 3: Default fallback
            return defaultApp;
        }

        /// <summary>
        /// Resolves the target application for a specific action in a navigation sequence.
        /// Considers the full sequence context to find the most recent Launch action.
        /// </summary>
        /// <param name="step">Workflow step definition</param>
        /// <param name="navigation">Navigation action configuration</param>
        /// <param name="actionIndex">Index of the action being resolved</param>
        /// <param name="defaultApp">Default application if resolution fails (default: "PlayOnline")</param>
        /// <returns>Target application name</returns>
        public static string ResolveForAction(
            WorkflowStepDefinition step,
            NavigationAction navigation,
            int actionIndex,
            string defaultApp = "PlayOnline")
        {
            if (step == null || navigation?.Sequence == null || actionIndex < 0 || actionIndex >= navigation.Sequence.Count)
                return defaultApp;

            var currentAction = navigation.Sequence[actionIndex];

            // Priority 1: Explicit TargetApplication parameter on current action
            var explicitTarget = currentAction.GetParameter<string>("TargetApplication", string.Empty);
            if (!string.IsNullOrWhiteSpace(explicitTarget))
                return explicitTarget;

            // Priority 2: Most recent Launch action earlier in the sequence
            for (int i = actionIndex - 1; i >= 0; i--)
            {
                var previousAction = navigation.Sequence[i];
                if (IsLaunchAction(previousAction))
                {
                    var appName = previousAction.GetParameter<string>("ApplicationName", string.Empty);
                    if (!string.IsNullOrWhiteSpace(appName))
                        return appName;
                }
            }

            // Priority 3: Fallback to step-level resolution
            return ResolveForStep(step, defaultApp);
        }

        /// <summary>
        /// Extracts application name from a template path with format "AppName/template.png".
        /// Returns the prefix before the first forward slash.
        /// </summary>
        /// <param name="templatePath">Template path (e.g., "Windower/main_menu.png")</param>
        /// <returns>Application name or empty string if not found</returns>
        public static string ExtractApplicationFromTemplatePath(string? templatePath)
        {
            if (string.IsNullOrWhiteSpace(templatePath))
                return string.Empty;

            // Check for forward slash separator
            var slashIndex = templatePath.IndexOf('/');
            if (slashIndex > 0)
                return templatePath.Substring(0, slashIndex).Trim();

            // Check for backslash separator (Windows paths)
            var backslashIndex = templatePath.IndexOf('\\');
            if (backslashIndex > 0)
                return templatePath.Substring(0, backslashIndex).Trim();

            return string.Empty;
        }

        /// <summary>
        /// Finds the first Launch action in a navigation sequence.
        /// </summary>
        private static KeyboardAction? FindLaunchAction(NavigationAction? navigation)
        {
            if (navigation?.Sequence == null)
                return null;

            return navigation.Sequence.FirstOrDefault(IsLaunchAction);
        }

        /// <summary>
        /// Checks if an action is a Launch action.
        /// </summary>
        private static bool IsLaunchAction(KeyboardAction action)
        {
            return string.Equals(action?.Action, "Launch", StringComparison.OrdinalIgnoreCase);
        }
    }
}
