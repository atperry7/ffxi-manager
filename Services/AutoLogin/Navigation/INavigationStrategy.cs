using System;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Services.AutoLogin.ScreenDetection;

namespace FFXIManager.Services.AutoLogin.Navigation
{
    /// <summary>
    /// Defines a strategy for UI navigation during auto-login processes.
    /// Implements Strategy pattern to support multiple navigation approaches
    /// (keyboard, clicking, hybrid) that are resolution and DPI independent.
    /// </summary>
    public interface INavigationStrategy
    {
        /// <summary>
        /// Executes the navigation action on the specified window.
        /// </summary>
        /// <param name="windowHandle">Handle of the window to interact with</param>
        /// <param name="action">Navigation action configuration</param>
        /// <param name="templateMatch">Optional template match result for relative positioning</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if navigation succeeded, false if it failed</returns>
        /// <remarks>
        /// Implementations should be idempotent and handle failures gracefully.
        /// Logging should be comprehensive for troubleshooting.
        /// </remarks>
        Task<bool> ExecuteAsync(
            IntPtr windowHandle,
            NavigationAction action,
            TemplateMatchResult? templateMatch,
            CancellationToken cancellationToken);

        /// <summary>
        /// Gets the name of this navigation strategy for logging purposes.
        /// </summary>
        string StrategyName { get; }
    }
}
