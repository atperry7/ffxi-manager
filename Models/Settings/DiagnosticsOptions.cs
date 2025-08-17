
namespace FFXIManager.Models.Settings
{
    public class DiagnosticsOptions
    {
        // Master toggle for diagnostics. When off, only Warning and Error are persisted
        public bool EnableDiagnostics { get; set; }
        // When diagnostics are enabled, include Debug-level events
        public bool VerboseLogging { get; set; }
        // Upper bound for in-memory log buffer and recent persisted entries to prevent flooding
        public int MaxLogEntries { get; set; } = 1000;
    }
}

