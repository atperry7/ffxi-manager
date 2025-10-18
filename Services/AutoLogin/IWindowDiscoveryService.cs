using System;
using System.Threading;
using System.Threading.Tasks;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Result of window discovery containing both PID and window handle.
    /// PID is the source of truth; window handle can be refreshed from PID.
    /// </summary>
    public class ProcessWindowInfo
    {
        /// <summary>Process ID - stable for process lifetime</summary>
        public int ProcessId { get; set; }

        /// <summary>Window handle - can become stale, refreshable from ProcessId</summary>
        public IntPtr WindowHandle { get; set; }

        /// <summary>Application name</summary>
        public string ApplicationName { get; set; } = string.Empty;

        /// <summary>Window title</summary>
        public string WindowTitle { get; set; } = string.Empty;

        /// <summary>Timestamp of when this info was captured</summary>
        public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Whether the window handle was valid at capture time</summary>
        public bool IsValid => ProcessId > 0 && WindowHandle != IntPtr.Zero;
    }

    /// <summary>
    /// Service for discovering and validating application window handles.
    /// Centralizes window discovery logic to prevent duplication across handlers.
    /// </summary>
    /// <remarks>
    /// **Architecture: PID-First Design**
    /// - Returns ProcessWindowInfo (PID + Handle) instead of just handle
    /// - PIDs are stable; window handles can become stale
    /// - Provides methods to refresh window handles from PIDs
    /// - Delegates to PlayOnlineMonitorService for POL/FFXI windows
    /// - Uses ExternalApplicationService + ProcessUtilityService for other apps
    ///
    /// **Benefits:**
    /// - Single responsibility for window discovery
    /// - Consistent window validation across all handlers
    /// - Reduces duplicate window discovery implementations
    /// - PID-first approach improves reliability in Windows 11
    /// - Easier to test and maintain
    /// </remarks>
    public interface IWindowDiscoveryService
    {
        /// <summary>
        /// Discovers process and window information for PlayOnline/FFXI application.
        /// Returns both PID (stable) and window handle (refreshable).
        /// </summary>
        /// <param name="context">Auto-login context containing potential PID hints</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Process window info with PID and handle</returns>
        /// <exception cref="InvalidOperationException">Thrown if no valid POL window found</exception>
        Task<ProcessWindowInfo> DiscoverPlayOnlineWindowInfoAsync(
            IAutoLoginContext context,
            CancellationToken cancellationToken);

        /// <summary>
        /// Discovers process and window information for an external application by name.
        /// Returns both PID (stable) and window handle (refreshable).
        /// </summary>
        /// <param name="applicationName">Name of the application (e.g., "Windower", "Ashita")</param>
        /// <param name="context">Auto-login context containing potential PID hints</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Process window info with PID and handle, or null if not found</returns>
        Task<ProcessWindowInfo?> DiscoverApplicationWindowInfoAsync(
            string applicationName,
            IAutoLoginContext context,
            CancellationToken cancellationToken);

        /// <summary>
        /// Discovers process and window information with automatic application type detection.
        /// Returns both PID (stable) and window handle (refreshable).
        /// </summary>
        /// <param name="applicationName">Application name or null/empty for PlayOnline default</param>
        /// <param name="context">Auto-login context containing potential PID hints</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Process window info with PID and handle</returns>
        Task<ProcessWindowInfo> DiscoverWindowInfoAsync(
            string? applicationName,
            IAutoLoginContext context,
            CancellationToken cancellationToken);

        /// <summary>
        /// Refreshes window handle from a known process ID.
        /// Use this to get a fresh window handle when the cached one may be stale.
        /// </summary>
        /// <param name="processId">Process ID to get window handle for</param>
        /// <param name="applicationName">Optional application name for logging</param>
        /// <returns>Fresh window handle or IntPtr.Zero if process has no valid windows</returns>
        Task<IntPtr> GetFreshWindowHandleFromPidAsync(int processId, string? applicationName = null);

        /// <summary>
        /// Legacy method: Discovers window handle only (backward compatibility).
        /// Prefer using DiscoverWindowInfoAsync for PID-first architecture.
        /// </summary>
        /// <param name="applicationName">Application name or null/empty for PlayOnline default</param>
        /// <param name="context">Auto-login context containing potential PID hints</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Valid window handle or IntPtr.Zero if not found</returns>
        Task<IntPtr> DiscoverWindowAsync(
            string? applicationName,
            IAutoLoginContext context,
            CancellationToken cancellationToken);
    }
}

