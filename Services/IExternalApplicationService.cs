using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services
{
    /// <summary>
    /// Interface for managing external applications
    /// </summary>
    public interface IExternalApplicationService
    {
        /// <summary>
        /// Event raised when an application's status changes
        /// </summary>
        event EventHandler<ExternalApplication>? ApplicationStatusChanged;

        /// <summary>
        /// Gets all configured external applications
        /// </summary>
        Task<List<ExternalApplication>> GetApplicationsAsync();

        /// <summary>
        /// Adds a new external application
        /// </summary>
        Task<ExternalApplication> AddApplicationAsync(ExternalApplication application);

        /// <summary>
        /// Updates an existing external application
        /// </summary>
        Task UpdateApplicationAsync(ExternalApplication application);

        /// <summary>
        /// Removes an external application
        /// </summary>
        Task RemoveApplicationAsync(ExternalApplication application);

        /// <summary>
        /// Launches an external application
        /// </summary>
        Task<bool> LaunchApplicationAsync(ExternalApplication application);

        /// <summary>
        /// Kills an external application
        /// </summary>
        Task<bool> KillApplicationAsync(ExternalApplication application);

        /// <summary>
        /// Refreshes the status of all applications
        /// </summary>
        Task RefreshApplicationStatusAsync();

        /// <summary>
        /// Refreshes the status of a specific application
        /// </summary>
        Task RefreshApplicationStatusAsync(ExternalApplication application);

        /// <summary>
        /// Starts monitoring for application status changes
        /// </summary>
        void StartMonitoring();

        /// <summary>
        /// Stops monitoring for application status changes
        /// </summary>
        void StopMonitoring();

        /// <summary>
        /// Finds an external application by name pattern matching.
        /// Searches both application name and executable path for the specified patterns.
        /// </summary>
        /// <param name="namePatterns">Array of patterns to match against application names</param>
        /// <param name="pathPatterns">Array of patterns to match against executable paths (optional)</param>
        /// <returns>The first matching application, or null if none found</returns>
        Task<ExternalApplication?> FindApplicationByPatternAsync(string[] namePatterns, string[]? pathPatterns = null);

        /// <summary>
        /// Finds an external application by a single name pattern.
        /// Convenience method for simple pattern matching.
        /// </summary>
        /// <param name="pattern">Pattern to match against application name or path</param>
        /// <returns>The first matching application, or null if none found</returns>
        Task<ExternalApplication?> FindApplicationByPatternAsync(string pattern);
    }
}
