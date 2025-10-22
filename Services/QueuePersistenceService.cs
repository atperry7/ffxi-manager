using FFXIManager.Models;
using FFXIManager.Models.Settings;

namespace FFXIManager.Services
{
    /// <summary>
    /// Implementation of queue persistence operations
    /// </summary>
    public class QueuePersistenceService : IQueuePersistenceService
    {
        private readonly ISettingsService _settingsService;
        private readonly ILoggingService _loggingService;
        private readonly IProfileService _profileService;
        private readonly IPlayOnlineMemberAccountService _accountService;

        public QueuePersistenceService(
            ISettingsService settingsService,
            ILoggingService loggingService,
            IProfileService profileService,
            IPlayOnlineMemberAccountService accountService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));

            // Load configuration from settings
            LoadConfigurationFromSettings();
        }

        #region Properties

        public bool AutoSaveQueueState { get; set; } = true;

        #endregion

        #region Persistence Operations

        public async Task SaveQueueStateAsync(IEnumerable<AutoLoginQueueItem> queueItems,
            bool isExecuting,
            bool isPaused,
            string? originalProfilePath,
            QueueExecutionStatistics statistics)
        {
            try
            {
                var settings = _settingsService.LoadSettings();

                settings.QueueState = new AutoLoginQueueState
                {
                    QueueItems = queueItems.Select(SerializableQueueItem.FromQueueItem).ToList(),
                    WasExecuting = isExecuting,
                    WasPaused = isPaused,
                    OriginalProfilePath = originalProfilePath,
                    LastSaved = DateTime.UtcNow,
                    Statistics = statistics
                };

                _settingsService.SaveSettings(settings);
                await _loggingService.LogDebugAsync("Saved auto-login queue state");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error saving queue state", ex);
            }
        }

        public async Task<(IList<AutoLoginQueueItem> items, string? originalProfilePath)> LoadQueueStateAsync()
        {
            try
            {
                var settings = _settingsService.LoadSettings();
                if (settings.QueueState == null)
                {
                    return (new List<AutoLoginQueueItem>(), null);
                }

                var queueState = settings.QueueState;

                // Restore queue items
                var restoredItems = new List<AutoLoginQueueItem>();
                foreach (var serializedItem in queueState.QueueItems)
                {
                    var account = await FindAccountByIdAsync(serializedItem.AccountId, serializedItem.ProfilePath);
                    var profile = await FindProfileByPathAsync(serializedItem.ProfilePath);

                    if (account != null && profile != null)
                    {
                        // Preserve completed status during session, only reset on application restart
                        var restoredStatus = serializedItem.Status == AutoLoginQueueStatus.Completed
                            ? AutoLoginQueueStatus.Completed
                            : AutoLoginQueueStatus.Pending;

                        var item = new AutoLoginQueueItem
                        {
                            Id = serializedItem.Id,
                            Account = account,
                            Profile = profile,
                            Position = serializedItem.Position,
                            Status = restoredStatus,
                            StartTime = restoredStatus == AutoLoginQueueStatus.Completed ? serializedItem.StartTime : null,
                            EndTime = restoredStatus == AutoLoginQueueStatus.Completed ? serializedItem.EndTime : null,
                            ErrorMessage = restoredStatus == AutoLoginQueueStatus.Completed ? (serializedItem.ErrorMessage ?? string.Empty) : string.Empty,
                            StatusMessage = restoredStatus == AutoLoginQueueStatus.Completed ? (serializedItem.StatusMessage ?? "Completed") : "Ready to start"
                        };

                        restoredItems.Add(item);
                    }
                }

                // Log the reset behavior for user awareness
                if (restoredItems.Count > 0)
                {
                    await _loggingService.LogInfoAsync($"Queue state loaded: {restoredItems.Count} items reset to pending for fresh auto-login session");
                }

                await _loggingService.LogInfoAsync($"Loaded auto-login queue state with {restoredItems.Count} items");

                return (restoredItems.OrderBy(x => x.Position).ToList(), queueState.OriginalProfilePath);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error loading queue state", ex);
                return (new List<AutoLoginQueueItem>(), null);
            }
        }

        public async Task ClearSavedStateAsync()
        {
            try
            {
                var settings = _settingsService.LoadSettings();
                settings.QueueState = null;
                _settingsService.SaveSettings(settings);
                await _loggingService.LogDebugAsync("Cleared saved queue state");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error clearing saved queue state", ex);
            }
        }

        public async Task SaveAllTimeStatisticsAsync(QueueExecutionStatistics statistics)
        {
            try
            {
                var settings = _settingsService.LoadSettings();
                settings.AllTimeStatistics = statistics;
                _settingsService.SaveSettings(settings);
                await _loggingService.LogDebugAsync("Saved all-time statistics");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error saving all-time statistics", ex);
            }
        }

        public async Task<QueueExecutionStatistics?> LoadAllTimeStatisticsAsync()
        {
            try
            {
                var settings = _settingsService.LoadSettings();
                return settings.AllTimeStatistics;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error loading all-time statistics", ex);
                return null;
            }
        }

        #endregion

        #region Helper Methods

        public async Task<PlayOnlineMemberAccount?> FindAccountByIdAsync(Guid accountId, string profilePath)
        {
            try
            {
                var accounts = await _accountService.GetAccountsForProfileAsync(profilePath);
                return accounts.FirstOrDefault(a => a.Id == accountId);
            }
            catch
            {
                return null;
            }
        }

        public async Task<ProfileInfo?> FindProfileByPathAsync(string profilePath)
        {
            try
            {
                var profiles = await _profileService.GetProfilesAsync();
                return profiles.FirstOrDefault(p => p.FilePath == profilePath);
            }
            catch
            {
                return null;
            }
        }

        private void LoadConfigurationFromSettings()
        {
            var settings = _settingsService.LoadSettings();
            AutoSaveQueueState = settings.AutoSaveQueueState;
        }

        #endregion
    }
}
