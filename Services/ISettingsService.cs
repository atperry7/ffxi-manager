using System.Threading.Tasks;

namespace FFXIManager.Services
{
    /// <summary>
    /// Simple interface for settings management
    /// </summary>
    public interface ISettingsService
    {
        ApplicationSettings LoadSettings();
        void SaveSettings(ApplicationSettings settings);
    }
}