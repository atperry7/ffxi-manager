using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Factory for resolving workflow action executors based on action type.
    /// Implements the Strategy Pattern to enable extensible, data-driven workflow execution.
    /// </summary>
    /// <remarks>
    /// **Architecture:**
    /// - Registers all IWorkflowActionExecutor implementations via DI
    /// - Resolves executors by matching ActionType property
    /// - Supports extensibility - new executors registered in DI are automatically available
    ///
    /// **Supported Actions:**
    /// - Launch: LaunchActionExecutor
    /// - Click: ClickActionExecutor
    /// - Tab, Enter, etc.: KeyboardActionExecutor (matches all keyboard keys)
    /// - Wait: WaitActionExecutor
    /// </remarks>
    public class WorkflowActionExecutorFactory : IWorkflowActionExecutorFactory
    {
        private readonly IEnumerable<IWorkflowActionExecutor> _executors;
        private readonly ILoggingService _loggingService;
        private readonly Dictionary<string, IWorkflowActionExecutor> _executorCache;

        /// <summary>
        /// List of keyboard action names that route to KeyboardActionExecutor
        /// </summary>
        private static readonly HashSet<string> KeyboardActions = new(StringComparer.OrdinalIgnoreCase)
        {
            "Tab", "Enter", "Return", "Escape", "Esc", "Spacebar", "Space",
            "DownArrow", "Down", "UpArrow", "Up", "LeftArrow", "Left", "RightArrow", "Right",
            "Home", "End", "PageUp", "PageDown"
        };

        public WorkflowActionExecutorFactory(
            IEnumerable<IWorkflowActionExecutor> executors,
            ILoggingService loggingService)
        {
            _executors = executors ?? throw new ArgumentNullException(nameof(executors));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _executorCache = new Dictionary<string, IWorkflowActionExecutor>(StringComparer.OrdinalIgnoreCase);

            // Pre-populate cache
            foreach (var executor in _executors)
            {
                _executorCache[executor.ActionType] = executor;
            }
        }

        /// <summary>
        /// Gets the appropriate executor for a given action type
        /// </summary>
        public IWorkflowActionExecutor GetExecutor(string actionType)
        {
            if (string.IsNullOrWhiteSpace(actionType))
            {
                throw new ArgumentException("Action type cannot be null or empty", nameof(actionType));
            }

            // Direct match (Launch, Click, Wait)
            if (_executorCache.TryGetValue(actionType, out var executor))
            {
                return executor;
            }

            // Keyboard action fallback (Tab, Enter, etc. -> KeyboardActionExecutor)
            if (IsKeyboardAction(actionType))
            {
                if (_executorCache.TryGetValue("Keyboard", out var keyboardExecutor))
                {
                    return keyboardExecutor;
                }

                throw new InvalidOperationException("KeyboardActionExecutor not registered in DI container");
            }

            // No executor found
            var availableExecutors = string.Join(", ", _executorCache.Keys);
            throw new InvalidOperationException($"No executor registered for action type '{actionType}'. Available executors: {availableExecutors}");
        }

        /// <summary>
        /// Determines if an action is a keyboard input action
        /// </summary>
        private bool IsKeyboardAction(string actionType)
        {
            return KeyboardActions.Contains(actionType);
        }

        /// <summary>
        /// Gets all registered action types
        /// </summary>
        public IEnumerable<string> GetRegisteredActionTypes()
        {
            var types = new List<string>(_executorCache.Keys);
            types.AddRange(KeyboardActions); // Include keyboard actions
            return types.Distinct(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Interface for the action executor factory
    /// </summary>
    public interface IWorkflowActionExecutorFactory
    {
        /// <summary>
        /// Gets the appropriate executor for a given action type
        /// </summary>
        IWorkflowActionExecutor GetExecutor(string actionType);

        /// <summary>
        /// Gets all registered action types
        /// </summary>
        IEnumerable<string> GetRegisteredActionTypes();
    }
}
