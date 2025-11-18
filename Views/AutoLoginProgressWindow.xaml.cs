using System;
using System.ComponentModel;
using System.Windows;
using FFXIManager.ViewModels;

namespace FFXIManager.Views;

/// <summary>
/// Compact always-on-top window showing auto-login progress.
/// Auto-opens when queue starts, auto-closes when queue completes.
/// </summary>
public partial class AutoLoginProgressWindow : Window
{
    private readonly AutoLoginProgressViewModel? _viewModel;

    public AutoLoginProgressWindow(AutoLoginProgressViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;

        // Subscribe to close request from ViewModel
        _viewModel.OnCloseRequested += OnViewModelCloseRequested;

        // Position window after it's loaded
        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        PositionWindowTopRight();
    }

    /// <summary>
    /// Positions the window in the top-right corner of the primary screen
    /// </summary>
    private void PositionWindowTopRight()
    {
        try
        {
            // Get the working area of the primary screen (excludes taskbar)
            var workArea = SystemParameters.WorkArea;

            // Position 10px from the right edge and 10px from the top
            Left = workArea.Right - Width - 10;
            Top = workArea.Top + 10;
        }
        catch
        {
            // Fallback: position relative to screen if anything goes wrong
            Left = SystemParameters.PrimaryScreenWidth - Width - 10;
            Top = 10;
        }
    }

    private void OnViewModelCloseRequested()
    {
        Close();
    }

    /// <summary>
    /// Handle title bar drag to move window
    /// </summary>
    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
        {
            DragMove();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Unsubscribe from ViewModel events
        if (_viewModel != null)
        {
            _viewModel.OnCloseRequested -= OnViewModelCloseRequested;
            _viewModel.Dispose();
        }

        base.OnClosing(e);
    }
}
