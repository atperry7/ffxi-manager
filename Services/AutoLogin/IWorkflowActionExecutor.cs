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
        /// Indicates whether this executor requires a valid window handle to operate.
        /// DynamicWorkflowHandler uses this to determine if on-demand window discovery is needed.
        /// </summary>
        /// <remarks>
        /// **When to return true:**
        /// - UI interaction actions (Click, InputPassword, InputOTP, MemberSlot, CharacterSlot)
        /// - Actions that capture screenshots for detection
        ///
        /// **When to return false:**
        /// - Pure workflow actions (Wait, Screenshot without window)
        /// - Process launch actions (Launch - operates at process level)
        /// - Keyboard actions (can operate without window, though may benefit from focus)
        /// </remarks>
        bool RequiresWindowHandle { get; }

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
    /// Context passed to action executors containing execution state.
    /// Implements PID-first architecture for improved reliability in Windows 11.
    /// </summary>
    /// <remarks>
    /// **PID-First Design:**
    /// - ProcessId is the source of truth (stable for process lifetime)
    /// - WindowHandle is a cached value that can become stale
    /// - Auto-refresh capability refreshes stale handles from ProcessId
    /// - WindowDiscoveryService used for on-demand handle refresh
    ///
    /// **Usage Pattern:**
    /// 1. Set ProcessId when launching applications
    /// 2. WindowHandle is cached and refreshable
    /// 3. Call EnsureFreshWindowHandleAsync() before UI operations
    /// 4. Handle is auto-refreshed if stale or invalid
    /// </remarks>
    public class WorkflowActionContext
    {
        private IntPtr _windowHandle;
        private DateTime _windowHandleRefreshTime = DateTime.MinValue;
        private const int HANDLE_REFRESH_THRESHOLD_SECONDS = 5;

        /// <summary>
        /// Process ID - stable identifier for the target application.
        /// Set this when launching applications; it remains valid for process lifetime.
        /// </summary>
        public int ProcessId { get; set; }

        /// <summary>
        /// Application name for logging and debugging purposes.
        /// </summary>
        public string? ApplicationName { get; set; }

        /// <summary>
        /// Window handle for UI automation (may be IntPtr.Zero for actions that don't need it).
        /// This is a cached value that can be refreshed from ProcessId if stale.
        /// </summary>
        public IntPtr WindowHandle
        {
            get => _windowHandle;
            set
            {
                _windowHandle = value;
                _windowHandleRefreshTime = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// When the window handle was last refreshed.
        /// Used to determine if handle needs re-validation.
        /// </summary>
        public DateTime WindowHandleRefreshTime => _windowHandleRefreshTime;

        /// <summary>
        /// Window discovery service for refreshing stale handles from ProcessId.
        /// Injected by DynamicWorkflowHandler.
        /// </summary>
        public IWindowDiscoveryService? WindowDiscoveryService { get; set; }

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

        /// <summary>
        /// Checks if the cached window handle is potentially stale and should be refreshed.
        /// A handle is considered stale if it's been cached for longer than the threshold.
        /// </summary>
        public bool IsWindowHandleStale()
        {
            if (_windowHandle == IntPtr.Zero) return true;
            if (ProcessId <= 0) return false; // Can't refresh without PID

            var age = DateTime.UtcNow - _windowHandleRefreshTime;
            return age.TotalSeconds > HANDLE_REFRESH_THRESHOLD_SECONDS;
        }

        /// <summary>
        /// Ensures the window handle is fresh and valid, refreshing from ProcessId if needed.
        /// Use this before performing UI automation operations to prevent stale handle errors.
        /// </summary>
        /// <returns>True if handle is valid, false if unable to get valid handle</returns>
        public async Task<bool> EnsureFreshWindowHandleAsync()
        {
            // If we don't have a PID, we can't refresh - just validate current handle
            if (ProcessId <= 0)
            {
                return _windowHandle != IntPtr.Zero;
            }

            // If handle is recent and non-zero, assume it's still valid
            if (!IsWindowHandleStale() && _windowHandle != IntPtr.Zero)
            {
                return true;
            }

            // Need to refresh - requires WindowDiscoveryService
            if (WindowDiscoveryService == null)
            {
                // Can't refresh without service, return current state
                return _windowHandle != IntPtr.Zero;
            }

            // Refresh handle from ProcessId
            var freshHandle = await WindowDiscoveryService.GetFreshWindowHandleFromPidAsync(ProcessId, ApplicationName);
            if (freshHandle != IntPtr.Zero)
            {
                WindowHandle = freshHandle; // Updates handle and refresh time
                return true;
            }

            // Failed to get fresh handle - process may have exited
            return false;
        }

        /// <summary>
        /// Updates context from ProcessWindowInfo (PID + handle).
        /// Recommended way to set both ProcessId and WindowHandle together.
        /// </summary>
        public void UpdateFromWindowInfo(ProcessWindowInfo info)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));

            ProcessId = info.ProcessId;
            ApplicationName = info.ApplicationName;
            WindowHandle = info.WindowHandle; // Also updates refresh time
        }
    }
}
