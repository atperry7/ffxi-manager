using FFXIManager.Models;

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
    /// **100% WORKFLOW-DRIVEN ARCHITECTURE**: Routes all subtasks to DynamicWorkflowHandler.
    /// - All UI navigation steps → DynamicWorkflowHandler
    /// - All application launch steps → DynamicWorkflowHandler
    /// </summary>
    public class LoginTaskHandlerResolver : ILoginTaskHandlerResolver
    {
        private readonly List<ILoginTaskHandler> _handlers;
        private readonly ILoggingService _loggingService;

        public LoginTaskHandlerResolver(
            IEnumerable<ILoginTaskHandler> handlers,
            ILoggingService loggingService)
        {
            _handlers = new List<ILoginTaskHandler>(handlers ?? throw new ArgumentNullException(nameof(handlers)));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));

            // Log registered handlers on startup
            _loggingService.LogInfoAsync($"LoginTaskHandlerResolver initialized with {_handlers.Count} handler(s)");
            foreach (var handler in _handlers)
            {
                var handlerType = handler is DynamicWorkflowHandler ? "Workflow-Driven (All Steps)" : handler.GetType().Name;
                _loggingService.LogDebugAsync($"Registered handler: {handler.GetType().Name} ({handlerType})");
            }
        }

        public ILoginTaskHandler? GetHandler(AutoLoginSubtask subtask)
        {
            if (subtask == null)
            {
                _loggingService.LogWarningAsync("GetHandler called with null subtask");
                return null;
            }

            _loggingService.LogDebugAsync($"Resolving handler for subtask: {subtask.Name} (HasWorkflowStep: {subtask.WorkflowStep != null})");

            // Find first handler that can handle this subtask
            var handler = _handlers.FirstOrDefault(h => h.CanHandle(subtask));

            if (handler != null)
            {
                var handlerType = handler is DynamicWorkflowHandler ? "Workflow" : handler.GetType().Name;
                _loggingService.LogInfoAsync($"✓ Using {handlerType} handler for subtask: {subtask.Name}");
                return handler;
            }

            // No handler found
            _loggingService.LogWarningAsync($"❌ No handler found for subtask: {subtask.Name}. Available handlers: {string.Join(", ", _handlers.Select(h => h.GetType().Name))}");
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