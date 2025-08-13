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
    }
}
