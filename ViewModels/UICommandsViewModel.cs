using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.ViewModels.Base;
using System.Windows.Input;

namespace FFXIManager.ViewModels
{
    /// <summary>
    /// ViewModel responsible for UI Commands (Copy, Open File Location, etc.)
    /// </summary>
    public class UICommandsViewModel : ViewModelBase
    {
        private readonly IUICommandService _uiCommandService;
        private readonly INotificationServiceEnhanced _notificationService;

        public UICommandsViewModel(
            IUICommandService uiCommandService,
            INotificationServiceEnhanced notificationService)
        {
            _uiCommandService = uiCommandService ?? throw new ArgumentNullException(nameof(uiCommandService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));

            InitializeCommands();
        }

        #region Commands

        public ICommand CopyProfileNameParameterCommand { get; private set; } = null!;
        public ICommand OpenFileLocationParameterCommand { get; private set; } = null!;

        private void InitializeCommands()
        {
            CopyProfileNameParameterCommand = new RelayCommandWithParameter<ProfileInfo>(
                profile => CopyProfileNameParameter(profile),
                profile => profile != null);
            OpenFileLocationParameterCommand = new RelayCommandWithParameter<ProfileInfo>(
                profile => OpenFileLocationParameter(profile),
                profile => profile != null);
        }

        #endregion

        #region Private Methods

        private async void CopyProfileNameParameter(ProfileInfo profile)
        {
            if (profile == null) return;

            try
            {
                _uiCommandService.CopyToClipboard(profile.Name);
                await _notificationService.ShowToastAsync($"Copied: {profile.Name}", NotificationType.Success, 2000);
            }
            catch (Exception ex)
            {
                await _notificationService.ShowToastAsync($"Copy failed: {ex.Message}", NotificationType.Error, 3000);
            }
        }

        private async void OpenFileLocationParameter(ProfileInfo profile)
        {
            if (profile == null) return;

            try
            {
                _uiCommandService.OpenFileLocation(profile.FilePath);
                await _notificationService.ShowToastAsync($"Opened location: {profile.Name}", NotificationType.Info, 2000);
            }
            catch (Exception ex)
            {
                await _notificationService.ShowToastAsync($"Open failed: {ex.Message}", NotificationType.Error, 3000);
            }
        }

        #endregion
    }
}
