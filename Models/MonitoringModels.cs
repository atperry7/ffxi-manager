using System;
using System.Collections.Generic;

namespace FFXIManager.Models
{
    /// <summary>
    /// Monitoring profile that defines what a consumer wants to track
    /// </summary>
    public class MonitoringProfile
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string[] ProcessNames { get; set; } = Array.Empty<string>();
        public bool TrackWindows { get; set; }
        public bool TrackWindowTitles { get; set; }
        public bool IncludeProcessPath { get; set; }
        public object? Context { get; set; } // Consumer-specific data (e.g., app config)
    }

    /// <summary>
    /// Represents a monitored process with all available information
    /// </summary>
    public class MonitoredProcess
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string? ExecutablePath { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime LastSeen { get; set; }
        public bool IsResponding { get; set; } = true;
        
        // Window information (if tracked)
        public List<MonitoredWindow> Windows { get; set; } = new();
        
        // Associated monitoring profiles
        public HashSet<Guid> MonitorIds { get; set; } = new();
        
        // Context data from profiles
        public Dictionary<Guid, object?> ContextData { get; set; } = new();
    }

    /// <summary>
    /// Represents a window belonging to a monitored process
    /// </summary>
    public class MonitoredWindow
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; } = string.Empty;
        public bool IsMainWindow { get; set; }
        public bool IsVisible { get; set; }
        public bool IsActive { get; set; } // True if this is the foreground window
        public DateTime LastTitleUpdate { get; set; }
    }

    /// <summary>
    /// Event args for monitored process events
    /// </summary>
    public class MonitoredProcessEventArgs : EventArgs
    {
        public Guid MonitorId { get; set; }
        public MonitoredProcess Process { get; set; } = null!;
        public MonitoringProfile Profile { get; set; } = null!;
    }
}
