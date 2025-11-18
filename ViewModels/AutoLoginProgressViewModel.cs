using System;
using System.Windows.Input;
using FFXIManager.Infrastructure;
using FFXIManager.ViewModels.Base;

namespace FFXIManager.ViewModels;

/// <summary>
/// ViewModel for the compact auto-login progress window.
/// Wraps AutoLoginQueueViewModel to provide progress data for the floating window.
/// </summary>
public class AutoLoginProgressViewModel : ViewModelBase, IDisposable
{
    private readonly AutoLoginQueueViewModel _queueViewModel;
    private bool _disposed;

    public AutoLoginProgressViewModel(AutoLoginQueueViewModel queueViewModel)
    {
        _queueViewModel = queueViewModel ?? throw new ArgumentNullException(nameof(queueViewModel));

        // Subscribe to property changes in the queue ViewModel
        _queueViewModel.PropertyChanged += OnQueueViewModelPropertyChanged;

        CloseWindowCommand = new RelayCommand(() => OnCloseRequested?.Invoke());
    }

    /// <summary>
    /// Event fired when the window should be closed (user clicked close or manual close requested)
    /// </summary>
    public event Action? OnCloseRequested;

    #region Wrapper Properties

    /// <summary>
    /// Display name of the currently logging in account
    /// </summary>
    public string CurrentAccountName => _queueViewModel.CurrentItem?.DisplayName ?? "No account";

    /// <summary>
    /// Name of the profile being used
    /// </summary>
    public string CurrentProfileName => _queueViewModel.CurrentItem?.ProfileName ?? string.Empty;

    /// <summary>
    /// Current step/subtask description
    /// </summary>
    public string CurrentStep => _queueViewModel.CurrentItem?.CurrentSubtaskDisplay ?? "Waiting...";

    /// <summary>
    /// Task progress percentage (0-100)
    /// </summary>
    public int TaskProgress => _queueViewModel.CurrentItem?.TaskProgress ?? 0;

    /// <summary>
    /// Elapsed time for current item
    /// </summary>
    public string ElapsedTime => _queueViewModel.CurrentItem?.DurationDisplay ?? "00:00";

    /// <summary>
    /// Current status message
    /// </summary>
    public string StatusMessage => _queueViewModel.CurrentItem?.StatusMessage ?? string.Empty;

    /// <summary>
    /// Queue status display (e.g., "Processing 2 of 5")
    /// </summary>
    public string QueueStatus => _queueViewModel.QueueProgressDisplay ?? string.Empty;

    /// <summary>
    /// Overall queue progress (0-100)
    /// </summary>
    public int OverallProgress => _queueViewModel.OverallProgress;

    /// <summary>
    /// Whether there is an active item being processed
    /// </summary>
    public bool HasCurrentItem => _queueViewModel.CurrentItem != null;

    #endregion

    #region Commands

    public ICommand CloseWindowCommand { get; }

    /// <summary>
    /// Command to stop the queue execution from the progress window
    /// </summary>
    public ICommand StopQueueCommand => _queueViewModel.StopQueueCommand;

    #endregion

    #region Event Handlers

    private void OnQueueViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Forward property changes from queue ViewModel to this ViewModel
        // This ensures UI updates when the underlying data changes
        switch (e.PropertyName)
        {
            case nameof(AutoLoginQueueViewModel.CurrentItem):
                OnPropertyChanged(nameof(CurrentAccountName));
                OnPropertyChanged(nameof(CurrentProfileName));
                OnPropertyChanged(nameof(CurrentStep));
                OnPropertyChanged(nameof(TaskProgress));
                OnPropertyChanged(nameof(ElapsedTime));
                OnPropertyChanged(nameof(StatusMessage));
                OnPropertyChanged(nameof(HasCurrentItem));
                break;

            case nameof(AutoLoginQueueViewModel.QueueProgressDisplay):
                OnPropertyChanged(nameof(QueueStatus));
                break;

            case nameof(AutoLoginQueueViewModel.OverallProgress):
                OnPropertyChanged(nameof(OverallProgress));
                break;
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        // Unsubscribe from events to prevent memory leaks
        _queueViewModel.PropertyChanged -= OnQueueViewModelPropertyChanged;

        GC.SuppressFinalize(this);
    }

    #endregion
}
