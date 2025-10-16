using System;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Models.AutoLogin;
using FFXIManager.Services.AutoLogin.ScreenDetection;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Defines the interface for executing workflow actions (Launch, Click, Keyboard, Wait, etc.).
    /// Implements the Strategy Pattern to enable extensible, data-driven workflow execution.
    /// </summary>
    /// <remarks>
    /// **Architecture:**
    /// Each action type (Launch, Click, Tab, Wait) has its own executor implementation.
    /// DynamicWorkflowHandler routes actions to appropriate executors based on Action property.
    ///
    /// **Benefits:**
    /// - Open/Closed Principle: Add new actions without modifying handler code
    /// - Single Responsibility: Each executor handles one action type
    /// - Testability: Executors can be unit tested independently
    /// - Data-Driven: Action types defined in JSON, not hardcoded
    /// </remarks>
    public interface IWorkflowActionExecutor
    {
        /// <summary>
        /// The action type this executor handles (e.g., "Launch", "Click", "Tab", "Wait")
        /// Used by factory to route actions to correct executor
        /// </summary>
        string ActionType { get; }

        /// <summary>
        /// Executes the workflow action
        /// </summary>
        /// <param name="action">The action definition with parameters</param>
        /// <param name="context">Execution context containing window handle, template match, etc.</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if action executed successfully, false otherwise</returns>
        Task<bool> ExecuteAsync(
            KeyboardAction action,
            WorkflowActionContext context,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Context passed to action executors containing execution state
    /// </summary>
    public class WorkflowActionContext
    {
        /// <summary>
        /// Window handle for UI automation (may be IntPtr.Zero for actions that don't need it)
        /// </summary>
        public IntPtr WindowHandle { get; set; }

        /// <summary>
        /// Template match result from screen detection (may be null if no template was matched)
        /// </summary>
        public TemplateMatchResult? TemplateMatch { get; set; }

        /// <summary>
        /// Auto-login subtask being executed (provides access to progress updates)
        /// </summary>
        public AutoLoginSubtask? Subtask { get; set; }

        /// <summary>
        /// Auto-login queue item being processed
        /// </summary>
        public AutoLoginQueueItem? QueueItem { get; set; }

        /// <summary>
        /// Auto-login context for storing/retrieving execution data
        /// </summary>
        public IAutoLoginContext? AutoLoginContext { get; set; }

        /// <summary>
        /// Workflow step definition being executed
        /// </summary>
        public WorkflowStepDefinition? WorkflowStep { get; set; }
    }
}
