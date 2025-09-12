
namespace FFXIManager.Models.Settings
{
    /// <summary>
    /// Legacy diagnostics options. 
    /// DEPRECATED: These settings are now superseded by Serilog configuration in appsettings.json.
    /// Maintained for backward compatibility during migration period.
    /// </summary>
    public class DiagnosticsOptions
    {
        /// <summary>
        /// Master toggle for diagnostics. When off, only Warning and Error are persisted.
        /// DEPRECATED: Use Serilog MinimumLevel configuration instead.
        /// </summary>
        public bool EnableDiagnostics { get; set; }
        
        /// <summary>
        /// When diagnostics are enabled, include Debug-level events.
        /// DEPRECATED: Use Serilog MinimumLevel configuration instead.
        /// </summary>
        public bool VerboseLogging { get; set; }
        
        /// <summary>
        /// Upper bound for in-memory log buffer and recent persisted entries to prevent flooding.
        /// DEPRECATED: Serilog handles file management automatically. This only affects GetRecentLogsAsync buffer size.
        /// </summary>
        public int MaxLogEntries { get; set; } = 1000;
    }
}

