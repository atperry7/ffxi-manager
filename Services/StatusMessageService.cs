using System;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace FFXIManager.Services
{
    /// <summary>
    /// Service for managing status messages with auto-clear functionality
    /// </summary>
    public interface IStatusMessageService
    {
        string CurrentMessage { get; }
        event EventHandler<string> MessageChanged;

        void SetMessage(string message);
        void SetTemporaryMessage(string message, TimeSpan duration);
        void Clear();
    }

    public class StatusMessageService : IStatusMessageService
    {
        private string _currentMessage = string.Empty;
        private DispatcherTimer? _clearTimer;

        public string CurrentMessage => _currentMessage;
        public event EventHandler<string>? MessageChanged;

        public void SetMessage(string message)
        {
            _clearTimer?.Stop();
            _currentMessage = message;
            MessageChanged?.Invoke(this, message);
        }

        public void SetTemporaryMessage(string message, TimeSpan duration)
        {
            SetMessage(message);

            _clearTimer?.Stop();
            _clearTimer = new DispatcherTimer { Interval = duration };
            _clearTimer.Tick += (s, e) =>
            {
                _clearTimer.Stop();
                Clear();
            };
            _clearTimer.Start();
        }

        public void Clear()
        {
            _clearTimer?.Stop();
            _currentMessage = string.Empty;
            MessageChanged?.Invoke(this, string.Empty);
        }
    }
}
