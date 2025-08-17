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
        private readonly IStatusMessageService _statusService;

        public UICommandsViewModel(
            IUICommandService uiCommandService,
            IStatusMessageService statusService)
        {
            _uiCommandService = uiCommandService ?? throw new ArgumentNullException(nameof(uiCommandService));
            _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));

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

        private void CopyProfileNameParameter(ProfileInfo profile)
        {
            if (profile == null) return;

            try
            {
                _uiCommandService.CopyToClipboard(profile.Name);
                _statusService.SetMessage($"Copied profile name: {profile.Name}");
            }
            catch (Exception ex)
            {
                _statusService.SetMessage($"Error copying to clipboard: {ex.Message}");
            }
        }

        private void OpenFileLocationParameter(ProfileInfo profile)
        {
            if (profile == null) return;

            try
            {
                _uiCommandService.OpenFileLocation(profile.FilePath);
                _statusService.SetMessage($"Opened file location for: {profile.Name}");
            }
            catch (Exception ex)
            {
                _statusService.SetMessage($"{ex.Message}");
            }
        }

        #endregion
    }
}
