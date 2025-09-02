using System;
using System.ComponentModel;
using System.Windows.Input;
using FFXIManager.Infrastructure;
using FFXIManager.Services;
using FFXIManager.ViewModels.Base;

namespace FFXIManager.ViewModels.CharacterMonitor
{
    /// <summary>
    /// Lightweight ViewModel for the embedded Character Monitor in ProfileActionsView.
    /// Reuses the CharacterCollectionViewModel for consistency.
    /// </summary>
    public class EmbeddedCharacterMonitorViewModel : ViewModelBase, IDisposable
    {
        private readonly IStatusMessageService _statusService;
        private readonly ILoggingService _loggingService;
        private bool _disposed;

        public EmbeddedCharacterMonitorViewModel()
        {
            // Get services
            var monitorService = ServiceLocator.PlayOnlineMonitorService;
            var orderingService = ServiceLocator.CharacterOrderingService;
            var activationService = ServiceLocator.HotkeyActivationService;
            _statusService = ServiceLocator.StatusMessageService;
            _loggingService = ServiceLocator.LoggingService;
            
            // Create the collection view model (shared logic with main window)
            CollectionViewModel = new CharacterCollectionViewModel(
                monitorService,
                orderingService,
                activationService,
                _statusService,
                _loggingService);
            
            // Subscribe to property changes for UI updates
            CollectionViewModel.PropertyChanged += OnCollectionPropertyChanged;
            
            InitializeCommands();
            
            _ = _loggingService.LogInfoAsync(
                "Embedded Character Monitor initialized", 
                "EmbeddedCharacterMonitorViewModel");
        }

        #region Properties

        /// <summary>
        /// The character collection view model
        /// </summary>
        public CharacterCollectionViewModel CollectionViewModel { get; }

        /// <summary>
        /// Monitoring status text
        /// </summary>
        public string MonitoringStatus => CollectionViewModel.CharacterCount > 0 
            ? "Monitoring Active" 
            : "No Characters";

        /// <summary>
        /// Character count for display
        /// </summary>
        public int CharacterCount => CollectionViewModel.CharacterCount;

        /// <summary>
        /// Quick access to characters collection for binding
        /// </summary>
        public System.Collections.ObjectModel.ObservableCollection<CharacterItemViewModel> Characters 
            => CollectionViewModel.Characters;

        #endregion

        #region Commands

        /// <summary>
        /// Command to refresh characters
        /// </summary>
        public ICommand RefreshCharactersCommand => CollectionViewModel.RefreshCommand;

        /// <summary>
        /// Command to activate a character
        /// </summary>
        public ICommand ActivateCharacterCommand => CollectionViewModel.ActivateCharacterCommand;

        /// <summary>
        /// Command to move character up
        /// </summary>
        public ICommand MoveCharacterUpCommand => CollectionViewModel.MoveCharacterUpCommand;

        /// <summary>
        /// Command to move character down
        /// </summary>
        public ICommand MoveCharacterDownCommand => CollectionViewModel.MoveCharacterDownCommand;

        /// <summary>
        /// Command to show the character monitor window
        /// </summary>
        public ICommand ShowCharacterMonitorCommand { get; private set; } = null!;

        private void InitializeCommands()
        {
            ShowCharacterMonitorCommand = new RelayCommand(ShowCharacterMonitorWindow);
        }

        #endregion

        #region Methods

        private void ShowCharacterMonitorWindow()
        {
            try
            {
                _statusService.SetMessage("Opening character monitor window...");
                Views.CharacterMonitorHelper.ShowCharacterMonitor();
                _statusService.SetMessage("Character monitor window opened");
            }
            catch (Exception ex)
            {
                _statusService.SetMessage($"Error opening character monitor: {ex.Message}");
                _ = _loggingService.LogErrorAsync(
                    "Error opening character monitor window", 
                    ex, 
                    "EmbeddedCharacterMonitorViewModel");
            }
        }

        private void OnCollectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(CollectionViewModel.CharacterCount):
                    OnPropertyChanged(nameof(CharacterCount));
                    OnPropertyChanged(nameof(MonitoringStatus));
                    break;
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;
            
            // Unsubscribe from events
            if (CollectionViewModel != null)
            {
                CollectionViewModel.PropertyChanged -= OnCollectionPropertyChanged;
                CollectionViewModel.Dispose();
            }
            
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}