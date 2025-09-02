using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using FFXIManager.Infrastructure;
using FFXIManager.Models.Settings;
using FFXIManager.Services;
using FFXIManager.ViewModels.Base;

namespace FFXIManager.ViewModels.CharacterMonitor
{
    /// <summary>
    /// ViewModel for the Character Monitor window UI state.
    /// Handles window properties, sizing, positioning, and visual settings.
    /// </summary>
    public class CharacterMonitorWindowViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private readonly ILoggingService _loggingService;
        
        private double _windowOpacity = 0.95;
        private bool _isAlwaysOnTop;
        private bool _isAutoHideEnabled;
        private bool _isClickToSwitchEnabled = true;
        private WindowState _windowState = WindowState.Normal;
        private double _width = 160;
        private double _height = 400;
        private double _left;
        private double _top;
        private bool _isMenuExpanded;

        public CharacterMonitorWindowViewModel(
            ISettingsService settingsService,
            ILoggingService loggingService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            
            LoadSettings();
            InitializeCommands();
        }

        #region Properties

        /// <summary>
        /// Window opacity (0.3 to 1.0)
        /// </summary>
        public double WindowOpacity
        {
            get => _windowOpacity;
            set
            {
                if (SetProperty(ref _windowOpacity, Math.Max(0.3, Math.Min(1.0, value))))
                {
                    SaveOpacitySetting();
                }
            }
        }

        /// <summary>
        /// Whether the window stays on top of other windows
        /// </summary>
        public bool IsAlwaysOnTop
        {
            get => _isAlwaysOnTop;
            set => SetProperty(ref _isAlwaysOnTop, value);
        }

        /// <summary>
        /// Whether auto-hide when no characters is enabled
        /// </summary>
        public bool IsAutoHideEnabled
        {
            get => _isAutoHideEnabled;
            set => SetProperty(ref _isAutoHideEnabled, value);
        }

        /// <summary>
        /// Whether clicking a character card switches to it
        /// </summary>
        public bool IsClickToSwitchEnabled
        {
            get => _isClickToSwitchEnabled;
            set
            {
                if (SetProperty(ref _isClickToSwitchEnabled, value))
                {
                    OnPropertyChanged(nameof(ShowSwitchButtons));
                }
            }
        }

        /// <summary>
        /// Whether to show explicit switch buttons (when click-to-switch is disabled)
        /// </summary>
        public bool ShowSwitchButtons => !IsClickToSwitchEnabled;

        /// <summary>
        /// Current window state
        /// </summary>
        public WindowState WindowState
        {
            get => _windowState;
            set => SetProperty(ref _windowState, value);
        }

        /// <summary>
        /// Window width
        /// </summary>
        public double Width
        {
            get => _width;
            set => SetProperty(ref _width, value);
        }

        /// <summary>
        /// Window height
        /// </summary>
        public double Height
        {
            get => _height;
            set => SetProperty(ref _height, value);
        }

        /// <summary>
        /// Window left position
        /// </summary>
        public double Left
        {
            get => _left;
            set => SetProperty(ref _left, value);
        }

        /// <summary>
        /// Window top position
        /// </summary>
        public double Top
        {
            get => _top;
            set => SetProperty(ref _top, value);
        }

        /// <summary>
        /// Whether the menu is expanded (for responsive design)
        /// </summary>
        public bool IsMenuExpanded
        {
            get => _isMenuExpanded;
            set => SetProperty(ref _isMenuExpanded, value);
        }

        /// <summary>
        /// Whether to use compact menu (based on window width)
        /// </summary>
        public bool UseCompactMenu => Width < 200;

        #endregion

        #region Commands

        public ICommand SetTinySizeCommand { get; private set; } = null!;
        public ICommand SetSmallSizeCommand { get; private set; } = null!;
        public ICommand SetMediumSizeCommand { get; private set; } = null!;
        public ICommand SetLargeSizeCommand { get; private set; } = null!;
        public ICommand SetWideSizeCommand { get; private set; } = null!;
        
        public ICommand DockTopLeftCommand { get; private set; } = null!;
        public ICommand DockTopRightCommand { get; private set; } = null!;
        public ICommand DockBottomLeftCommand { get; private set; } = null!;
        public ICommand DockBottomRightCommand { get; private set; } = null!;
        
        public ICommand MinimizeCommand { get; private set; } = null!;
        public ICommand CloseCommand { get; private set; } = null!;
        public ICommand ToggleMenuCommand { get; private set; } = null!;

        private void InitializeCommands()
        {
            // Size preset commands
            SetTinySizeCommand = new RelayCommand(() => SetPresetSize(120, 200, "Tiny"));
            SetSmallSizeCommand = new RelayCommand(() => SetPresetSize(160, 300, "Small"));
            SetMediumSizeCommand = new RelayCommand(() => SetPresetSize(200, 400, "Medium"));
            SetLargeSizeCommand = new RelayCommand(() => SetPresetSize(250, 500, "Large"));
            SetWideSizeCommand = new RelayCommand(() => SetPresetSize(400, 150, "Wide"));
            
            // Docking commands
            DockTopLeftCommand = new RelayCommand(() => DockToPosition(DockPosition.TopLeft));
            DockTopRightCommand = new RelayCommand(() => DockToPosition(DockPosition.TopRight));
            DockBottomLeftCommand = new RelayCommand(() => DockToPosition(DockPosition.BottomLeft));
            DockBottomRightCommand = new RelayCommand(() => DockToPosition(DockPosition.BottomRight));
            
            // Window commands
            MinimizeCommand = new RelayCommand(() => WindowState = WindowState.Minimized);
            CloseCommand = new RelayCommand(() => OnCloseRequested?.Invoke(this, EventArgs.Empty));
            ToggleMenuCommand = new RelayCommand(() => IsMenuExpanded = !IsMenuExpanded);
        }

        #endregion

        #region Events

        /// <summary>
        /// Raised when the window should be closed
        /// </summary>
        public event EventHandler? OnCloseRequested;

        #endregion

        #region Methods

        /// <summary>
        /// Sets a preset window size
        /// </summary>
        private void SetPresetSize(double width, double height, string sizeName)
        {
            Width = width;
            Height = height;
            
            _ = _loggingService.LogInfoAsync(
                $"Character monitor resized to {sizeName} ({width}x{height})", 
                "CharacterMonitorWindowViewModel");
        }

        /// <summary>
        /// Docks the window to a screen position
        /// </summary>
        private void DockToPosition(DockPosition position)
        {
            var workArea = SystemParameters.WorkArea;
            const int margin = 10;
            
            switch (position)
            {
                case DockPosition.TopLeft:
                    Left = workArea.Left + margin;
                    Top = workArea.Top + margin;
                    break;
                    
                case DockPosition.TopRight:
                    Left = workArea.Right - Width - margin;
                    Top = workArea.Top + margin;
                    break;
                    
                case DockPosition.BottomLeft:
                    Left = workArea.Left + margin;
                    Top = workArea.Bottom - Height - margin;
                    break;
                    
                case DockPosition.BottomRight:
                    Left = workArea.Right - Width - margin;
                    Top = workArea.Bottom - Height - margin;
                    break;
            }
            
            _ = _loggingService.LogInfoAsync(
                $"Window docked to {position} at ({Left}, {Top})", 
                "CharacterMonitorWindowViewModel");
        }

        /// <summary>
        /// Handles auto-hide logic based on character count
        /// </summary>
        public void UpdateAutoHideState(int characterCount)
        {
            if (!IsAutoHideEnabled) return;
            
            if (characterCount == 0 && WindowState != WindowState.Minimized)
            {
                WindowState = WindowState.Minimized;
                _ = _loggingService.LogInfoAsync(
                    "Character monitor auto-hidden (no characters running)", 
                    "CharacterMonitorWindowViewModel");
            }
            else if (characterCount > 0 && WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
                _ = _loggingService.LogInfoAsync(
                    $"Character monitor auto-restored ({characterCount} characters detected)", 
                    "CharacterMonitorWindowViewModel");
            }
        }

        /// <summary>
        /// Loads settings from the settings service
        /// </summary>
        private void LoadSettings()
        {
            try
            {
                var settings = _settingsService.LoadSettings();
                WindowOpacity = settings.CharacterMonitorOpacity;
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync(
                    "Failed to load character monitor settings", 
                    ex, 
                    "CharacterMonitorWindowViewModel");
            }
        }

        /// <summary>
        /// Saves opacity setting
        /// </summary>
        private void SaveOpacitySetting()
        {
            try
            {
                var settings = _settingsService.LoadSettings();
                settings.CharacterMonitorOpacity = WindowOpacity;
                _settingsService.SaveSettings(settings);
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync(
                    "Failed to save opacity setting", 
                    ex, 
                    "CharacterMonitorWindowViewModel");
            }
        }

        #endregion

        #region Enums

        private enum DockPosition
        {
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        #endregion
    }
}