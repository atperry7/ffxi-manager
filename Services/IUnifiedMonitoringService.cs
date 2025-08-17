using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services
{
    /// <summary>
    /// Interface for the unified monitoring service
    /// </summary>
    public interface IUnifiedMonitoringService
    {
        // Monitor registration
        Guid RegisterMonitor(MonitoringProfile profile);
        void UnregisterMonitor(Guid monitorId);
        void UpdateMonitorProfile(Guid monitorId, MonitoringProfile profile);

        // Query current state
        Task<List<MonitoredProcess>> GetProcessesAsync(Guid monitorId);
        Task<MonitoredProcess?> GetProcessAsync(Guid monitorId, int processId);

        // Control
        void StartMonitoring();
        void StopMonitoring();
        bool IsMonitoring { get; }

        // Events - filtered per monitor
        event EventHandler<MonitoredProcessEventArgs>? ProcessDetected;
        event EventHandler<MonitoredProcessEventArgs>? ProcessUpdated;
        event EventHandler<MonitoredProcessEventArgs>? ProcessRemoved;

        // Global events - for logging/debugging
        event EventHandler<MonitoredProcess>? GlobalProcessDetected;
        event EventHandler<MonitoredProcess>? GlobalProcessUpdated;
        event EventHandler<int>? GlobalProcessRemoved; // ProcessId
    }
}
