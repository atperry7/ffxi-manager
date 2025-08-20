using System;
using System.Windows;
using System.Windows.Input;
using FFXIManager.ViewModels.CharacterMonitor;

namespace FFXIManager.Views
{
    /// <summary>
    /// Refactored Character Monitor window with clean MVVM architecture.
    /// Code-behind is minimal, only handling window-specific behaviors that can't be easily bound.
    /// </summary>
    public partial class CharacterMonitorWindowV2 : Window
    {
        private CharacterMonitorViewModel? _viewModel;

        public CharacterMonitorWindowV2()
        {
            InitializeComponent();
            
            // Create and set the view model
            _viewModel = new CharacterMonitorViewModel();
            DataContext = _viewModel;
            
            // Subscribe to close request from view model
            _viewModel.OnCloseRequested += OnViewModelCloseRequested;
            
            // Set initial properties
            ShowInTaskbar = true;
        }

        /// <summary>
        /// Handle title bar left click for dragging
        /// </summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // Double-click toggles maximize/restore
                WindowState = WindowState == WindowState.Normal 
                    ? WindowState.Maximized 
                    : WindowState.Normal;
            }
            else
            {
                // Single click starts drag
                DragMove();
            }
        }

        /// <summary>
        /// Handle title bar right click for system menu
        /// </summary>
        private void TitleBar_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            SystemCommands.ShowSystemMenu(this, GetMousePosition());
            e.Handled = true;
        }

        /// <summary>
        /// Get current mouse position in screen coordinates
        /// </summary>
        private Point GetMousePosition()
        {
            var position = Mouse.GetPosition(this);
            return PointToScreen(position);
        }

        /// <summary>
        /// Handle close request from view model
        /// </summary>
        private void OnViewModelCloseRequested(object? sender, EventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Clean up when window is closed
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.OnCloseRequested -= OnViewModelCloseRequested;
                _viewModel.Dispose();
                _viewModel = null;
            }
            
            base.OnClosed(e);
        }
    }
}