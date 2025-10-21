using FFXIManager.Models;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace FFXIManager.Services
{
    /// <summary>
    /// Service for managing status messages with auto-clear functionality and history.
    /// </summary>
    public interface IStatusMessageService
    {
        string CurrentMessage { get; }
        event EventHandler<string> MessageChanged;

        // History/queue support
        IReadOnlyList<StatusMessageEntry> History { get; }
        event EventHandler<StatusMessageEntry> MessageEnqueued;
        int MaxHistory { get; set; }

        void SetMessage(string message);
        void SetTemporaryMessage(string message, TimeSpan duration);
        void Enqueue(string message, NotificationType type = NotificationType.Info);
        void Clear();
    }

    public class StatusMessageService : IStatusMessageService
    {
        private readonly ObservableCollection<StatusMessageEntry> _history = new();
        private string _currentMessage = string.Empty;
        private DispatcherTimer? _clearTimer;

        public string CurrentMessage => _currentMessage;
        public event EventHandler<string>? MessageChanged;

        public IReadOnlyList<StatusMessageEntry> History => _history;
        public event EventHandler<StatusMessageEntry>? MessageEnqueued;
        public int MaxHistory { get; set; } = 20;

        public void SetMessage(string message)
        {
            _clearTimer?.Stop();
            _currentMessage = message;
            MessageChanged?.Invoke(this, message);
            Enqueue(message, NotificationType.Info);
        }

        public void SetTemporaryMessage(string message, TimeSpan duration)
        {
            _clearTimer?.Stop();
            _currentMessage = message;
            MessageChanged?.Invoke(this, message);
            Enqueue(message, NotificationType.Info);

            _clearTimer = new DispatcherTimer { Interval = duration };
            _clearTimer.Tick += (s, e) =>
            {
                _clearTimer?.Stop();
                Clear();
            };
            _clearTimer.Start();
        }

        public void Enqueue(string message, NotificationType type = NotificationType.Info)
        {
            var entry = new StatusMessageEntry(message, type, DateTime.Now);
            _history.Add(entry);

            // Keep only the most recent entries up to MaxHistory
            while (_history.Count > MaxHistory)
            {
                _history.RemoveAt(0);
            }

            MessageEnqueued?.Invoke(this, entry);
        }

        public void Clear()
        {
            _clearTimer?.Stop();
            _currentMessage = string.Empty;
            MessageChanged?.Invoke(this, string.Empty);
        }
    }
}
