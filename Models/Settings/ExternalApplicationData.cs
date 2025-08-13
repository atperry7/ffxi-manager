
namespace FFXIManager.Models.Settings
{
    /// <summary>
    /// Simplified external application data for persistence
    /// </summary>
    public class ExternalApplicationData
    {
        public string Name { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public string WorkingDirectory { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public bool AllowMultipleInstances { get; set; }
    }
}

