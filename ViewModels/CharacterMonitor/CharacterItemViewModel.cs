using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using FFXIManager.Infrastructure;
using FFXIManager.Models;
using FFXIManager.ViewModels.Base;

namespace FFXIManager.ViewModels.CharacterMonitor
{
    /// <summary>
    /// ViewModel for individual character items in the monitor.
    /// Wraps a PlayOnlineCharacter with UI-specific properties and behaviors.
    /// </summary>
    public class CharacterItemViewModel : ViewModelBase
    {
        private readonly PlayOnlineCharacter _character;
        private int _slotIndex;
        private bool _isActivating;
        private bool _isLastActivated;
        private DateTime? _lastActivatedTime;

        public CharacterItemViewModel(PlayOnlineCharacter character, int slotIndex)
        {
            _character = character ?? throw new ArgumentNullException(nameof(character));
            _slotIndex = slotIndex;
            
            // Subscribe to character property changes
            _character.PropertyChanged += OnCharacterPropertyChanged;
            
            InitializeCommands();
        }

        #region Properties

        /// <summary>
        /// The underlying character model
        /// </summary>
        public PlayOnlineCharacter Character => _character;

        /// <summary>
        /// Display name for the character
        /// </summary>
        public string DisplayName => _character.DisplayName;

        /// <summary>
        /// Character's window title
        /// </summary>
        public string WindowTitle => _character.WindowTitle;

        /// <summary>
        /// Process ID
        /// </summary>
        public int ProcessId => _character.ProcessId;

        /// <summary>
        /// Window handle
        /// </summary>
        public IntPtr WindowHandle => _character.WindowHandle;

        /// <summary>
        /// Zero-based slot index (0-8 for F1-F9)
        /// </summary>
        public int SlotIndex
        {
            get => _slotIndex;
            set
            {
                if (SetProperty(ref _slotIndex, value))
                {
                    OnPropertyChanged(nameof(HotkeyNumber));
                    OnPropertyChanged(nameof(HotkeyText));
                }
            }
        }

        /// <summary>
        /// Display number for hotkey (1-9)
        /// </summary>
        public int HotkeyNumber => _slotIndex + 1;

        /// <summary>
        /// Hotkey text display (e.g., "F1", "F2")
        /// </summary>
        public string HotkeyText => $"F{HotkeyNumber}";

        /// <summary>
        /// Whether this character is currently being activated
        /// </summary>
        public bool IsActivating
        {
            get => _isActivating;
            set
            {
                if (SetProperty(ref _isActivating, value))
                {
                    OnPropertyChanged(nameof(StatusBrush));
                    OnPropertyChanged(nameof(BorderBrush));
                }
            }
        }

        /// <summary>
        /// Whether this was the last activated character
        /// </summary>
        public bool IsLastActivated
        {
            get => _isLastActivated;
            set
            {
                if (SetProperty(ref _isLastActivated, value))
                {
                    if (value)
                        _lastActivatedTime = DateTime.Now;
                    
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(StatusBrush));
                    OnPropertyChanged(nameof(BorderBrush));
                    OnPropertyChanged(nameof(BorderThickness));
                }
            }
        }

        /// <summary>
        /// Status text display
        /// </summary>
        public string StatusText
        {
            get
            {
                if (IsActivating)
                    return "Activating...";
                
                if (IsLastActivated && _lastActivatedTime.HasValue)
                {
                    var elapsed = DateTime.Now - _lastActivatedTime.Value;
                    if (elapsed.TotalSeconds < 60)
                        return $"Active ({elapsed.TotalSeconds:0}s ago)";
                    else if (elapsed.TotalMinutes < 60)
                        return $"Active ({elapsed.TotalMinutes:0}m ago)";
                    else
                        return "Active";
                }
                
                return _character.IsRunning ? "Running" : "Stopped";
            }
        }

        /// <summary>
        /// Status indicator brush
        /// </summary>
        public Brush StatusBrush
        {
            get
            {
                if (IsActivating)
                    return System.Windows.Media.Brushes.Orange;
                
                if (IsLastActivated)
                    return System.Windows.Media.Brushes.LimeGreen;
                
                return _character.IsRunning 
                    ? System.Windows.Media.Brushes.Green 
                    : System.Windows.Media.Brushes.Red;
            }
        }

        /// <summary>
        /// Border brush for visual feedback
        /// </summary>
        public Brush BorderBrush
        {
            get
            {
                if (IsLastActivated)
                    return System.Windows.Media.Brushes.LimeGreen;
                
                if (IsActivating)
                    return System.Windows.Media.Brushes.Orange;
                
                return System.Windows.Media.Brushes.Gray;
            }
        }

        /// <summary>
        /// Border thickness for emphasis
        /// </summary>
        public double BorderThickness => IsLastActivated ? 2.0 : 1.0;

        /// <summary>
        /// Whether the character process is running
        /// </summary>
        public bool IsRunning => _character.IsRunning;

        /// <summary>
        /// Can this character be moved up in order
        /// </summary>
        public bool CanMoveUp => _slotIndex > 0;

        /// <summary>
        /// Can this character be moved down in order
        /// </summary>
        public bool CanMoveDown { get; set; } // Set by parent collection

        #endregion

        #region Commands

        public ICommand ActivateCommand { get; private set; } = null!;

        private void InitializeCommands()
        {
            ActivateCommand = new RelayCommand(
                () => OnActivateRequested?.Invoke(this, EventArgs.Empty),
                () => IsRunning && !IsActivating);
        }

        #endregion

        #region Events

        /// <summary>
        /// Raised when activation is requested for this character
        /// </summary>
        public event EventHandler? OnActivateRequested;

        #endregion

        #region Methods

        /// <summary>
        /// Updates the status display (call periodically to update time displays)
        /// </summary>
        public void RefreshStatusDisplay()
        {
            OnPropertyChanged(nameof(StatusText));
        }

        /// <summary>
        /// Handle property changes from the underlying character
        /// </summary>
        private void OnCharacterPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(PlayOnlineCharacter.DisplayName):
                    OnPropertyChanged(nameof(DisplayName));
                    break;
                case nameof(PlayOnlineCharacter.WindowTitle):
                    OnPropertyChanged(nameof(WindowTitle));
                    break;
                case nameof(PlayOnlineCharacter.IsRunning):
                    OnPropertyChanged(nameof(IsRunning));
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(StatusBrush));
                    ((RelayCommand)ActivateCommand).RaiseCanExecuteChanged();
                    break;
            }
        }

        /// <summary>
        /// Clean up resources
        /// </summary>
        public void Dispose()
        {
            _character.PropertyChanged -= OnCharacterPropertyChanged;
        }

        #endregion
    }
}