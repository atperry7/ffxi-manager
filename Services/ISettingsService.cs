namespace FFXIManager.Services
{
    /// <summary>
    /// Interface for settings management
    /// </summary>
    public interface ISettingsService
    {
        ApplicationSettings LoadSettings();
        void SaveSettings(ApplicationSettings settings);
    }
}