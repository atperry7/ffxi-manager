using System;
using System.Collections.Generic;
using System.Linq;
using FFXIManager.Models;
using FFXIManager.Services;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Service responsible for resolving the appropriate handler for a given login task.
    /// Manages the collection of registered handlers and routes tasks to the correct implementation.
    /// </summary>
    public interface ILoginTaskHandlerResolver
    {
        /// <summary>
        /// Gets the appropriate handler for the specified subtask
        /// </summary>
        /// <param name="subtask">The subtask to find a handler for</param>
        /// <returns>The handler that can execute the subtask, or null if none found</returns>
        ILoginTaskHandler? GetHandler(AutoLoginSubtask subtask);

        /// <summary>
        /// Gets all registered handlers
        /// </summary>
        /// <returns>Collection of all available handlers</returns>
        IEnumerable<ILoginTaskHandler> GetAllHandlers();

        /// <summary>
        /// Registers a new handler with the resolver
        /// </summary>
        /// <param name="handler">The handler to register</param>
        void RegisterHandler(ILoginTaskHandler handler);
    }

    /// <summary>
    /// Default implementation of the login task handler resolver.
    /// **WORKFLOW-FIRST ARCHITECTURE**: Prioritizes data-driven DynamicWorkflowHandler,
    /// then falls back to specialized handlers for backward compatibility.
    /// </summary>
    public class LoginTaskHandlerResolver : ILoginTaskHandlerResolver
    {
        private readonly List<ILoginTaskHandler> _handlers;
        private readonly ILoggingService _loggingService;
        private readonly ILoginTaskHandler? _dynamicHandler;

        public LoginTaskHandlerResolver(
            IEnumerable<ILoginTaskHandler> handlers,
            ILoggingService loggingService)
        {
            _handlers = new List<ILoginTaskHandler>(handlers ?? throw new ArgumentNullException(nameof(handlers)));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));

            // Separate DynamicWorkflowHandler for priority execution
            _dynamicHandler = _handlers.FirstOrDefault(h => h is DynamicWorkflowHandler);

            // Log registered handlers on startup
            _loggingService.LogInfoAsync($"LoginTaskHandlerResolver initialized with {_handlers.Count} handlers (workflow-first mode)");
            foreach (var handler in _handlers)
            {
                var isDynamic = handler is DynamicWorkflowHandler;
                _loggingService.LogDebugAsync($"Registered handler: {handler.GetType().Name} for task step: {handler.TaskStep}{(isDynamic ? " (PRIMARY: workflow-based execution)" : " (FALLBACK: legacy handler)")}");
            }
        }

        public ILoginTaskHandler? GetHandler(AutoLoginSubtask subtask)
        {
            if (subtask == null)
            {
                _loggingService.LogWarningAsync("GetHandler called with null subtask");
                return null;
            }

            _loggingService.LogDebugAsync($"Resolving handler for subtask: {subtask.Name} (TaskStep: {subtask.TaskStep}, HasWorkflowStep: {subtask.WorkflowStep != null})");

            // **PHASE 1: WORKFLOW-FIRST** - Try DynamicWorkflowHandler for data-driven execution
            if (_dynamicHandler != null && _dynamicHandler.CanHandle(subtask))
            {
                _loggingService.LogInfoAsync($"✓ Using WORKFLOW-BASED handler for subtask: {subtask.Name}");
                return _dynamicHandler;
            }

            // **PHASE 2: LEGACY FALLBACK** - Try specialized handlers for backward compatibility
            var specializedHandler = _handlers
                .Where(h => h is not DynamicWorkflowHandler)
                .FirstOrDefault(h => h.CanHandle(subtask));

            if (specializedHandler != null)
            {
                _loggingService.LogInfoAsync($"⚠ Using LEGACY specialized handler: {specializedHandler.GetType().Name} for subtask: {subtask.Name}");
                return specializedHandler;
            }

            // **PHASE 3: NO HANDLER** - This should rarely happen now that workflows are primary
            _loggingService.LogWarningAsync($"❌ No handler found for subtask: {subtask.Name} (TaskStep: {subtask.TaskStep}). Available handlers: {string.Join(", ", _handlers.Select(h => h.GetType().Name))}");
            return null;
        }

        public IEnumerable<ILoginTaskHandler> GetAllHandlers()
        {
            return _handlers.AsReadOnly();
        }

        public void RegisterHandler(ILoginTaskHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            if (!_handlers.Contains(handler))
            {
                _handlers.Add(handler);
            }
        }
    }
}