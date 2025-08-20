using System;
using System.ComponentModel;
using System.Windows.Input;
using FFXIManager.Infrastructure;
using FFXIManager.Services;
using FFXIManager.ViewModels.Base;

namespace FFXIManager.ViewModels.CharacterMonitor
{
    /// <summary>
    /// Main ViewModel for the Character Monitor that coordinates window and collection view models.
    /// This is the top-level view model that the CharacterMonitorWindow will bind to.
    /// </summary>
    public class CharacterMonitorViewModel : ViewModelBase, IDisposable
    {
        private readonly ILoggingService _loggingService;
        private bool _disposed;

        public CharacterMonitorViewModel()
        {
            // Get services from ServiceLocator
            var settingsService = ServiceLocator.SettingsService;
            _loggingService = ServiceLocator.LoggingService;
            var monitorService = ServiceLocator.PlayOnlineMonitorService;
            var orderingService = ServiceLocator.CharacterOrderingService;
            var activationService = ServiceLocator.HotkeyActivationService;
            var statusService = ServiceLocator.StatusMessageService;
            
            // Initialize sub-view models
            WindowViewModel = new CharacterMonitorWindowViewModel(settingsService, _loggingService);
            CollectionViewModel = new CharacterCollectionViewModel(
                monitorService, 
                orderingService, 
                activationService, 
                statusService, 
                _loggingService);
            
            // Wire up events
            WindowViewModel.OnCloseRequested += OnWindowCloseRequested;
            CollectionViewModel.PropertyChanged += OnCollectionPropertyChanged;
            
            InitializeCommands();
            
            _ = _loggingService.LogInfoAsync(
                "Character Monitor initialized with new architecture", 
                "CharacterMonitorViewModel");
        }

        #region Properties

        /// <summary>
        /// ViewModel for window-specific properties and commands
        /// </summary>
        public CharacterMonitorWindowViewModel WindowViewModel { get; }

        /// <summary>
        /// ViewModel for character collection management
        /// </summary>
        public CharacterCollectionViewModel CollectionViewModel { get; }

        /// <summary>
        /// Quick access to character count for UI binding
        /// </summary>
        public int CharacterCount => CollectionViewModel.CharacterCount;

        /// <summary>
        /// Quick access to performance status
        /// </summary>
        public string PerformanceStatus => CollectionViewModel.PerformanceStatus;

        #endregion

        #region Commands

        /// <summary>
        /// Command to activate a character by slot (for hotkey integration)
        /// </summary>
        public ICommand ActivateSlotCommand { get; private set; } = null!;

        private void InitializeCommands()
        {
            ActivateSlotCommand = new RelayCommandWithParameter<int>(
                async slotIndex => await CollectionViewModel.ActivateCharacterBySlotAsync(slotIndex),
                slotIndex => slotIndex >= 0 && slotIndex < CollectionViewModel.CharacterCount);
        }

        #endregion

        #region Event Handlers

        private void OnWindowCloseRequested(object? sender, EventArgs e)
        {
            // Notify any listeners that the window wants to close
            OnCloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnCollectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(CollectionViewModel.CharacterCount):
                    OnPropertyChanged(nameof(CharacterCount));
                    // Update auto-hide state
                    WindowViewModel.UpdateAutoHideState(CharacterCount);
                    break;
                    
                case nameof(CollectionViewModel.PerformanceStatus):
                    OnPropertyChanged(nameof(PerformanceStatus));
                    break;
            }
        }

        #endregion

        #region Events

        /// <summary>
        /// Raised when the monitor window should be closed
        /// </summary>
        public event EventHandler? OnCloseRequested;

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;
            
            // Unsubscribe from events
            if (WindowViewModel != null)
            {
                WindowViewModel.OnCloseRequested -= OnWindowCloseRequested;
            }
            
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