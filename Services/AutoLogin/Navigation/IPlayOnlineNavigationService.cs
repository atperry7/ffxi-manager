using System;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services.AutoLogin.Navigation
{
    /// <summary>
    /// Interface for PlayOnline navigation service.
    /// Handles navigation through PlayOnline screens to launch Final Fantasy XI.
    /// </summary>
    public interface IPlayOnlineNavigationService
    {
        /// <summary>
        /// Navigates through PlayOnline screens to launch Final Fantasy XI.
        /// Handles main screen, game selection, play screens, and final confirmation.
        /// </summary>
        /// <param name="subtask">Subtask for progress reporting</param>
        /// <param name="windowHandle">Initial PlayOnline window handle</param>
        /// <param name="context">AutoLogin context for window handle tracking</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Final window handle after navigation</returns>
        Task<IntPtr> NavigateToFinalFantasyXIAsync(
            AutoLoginSubtask subtask,
            IntPtr windowHandle,
            IAutoLoginContext context,
            CancellationToken cancellationToken);
    }
}
