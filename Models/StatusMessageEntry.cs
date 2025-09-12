using System;
using FFXIManager.Services;

namespace FFXIManager.Models
{
    /// <summary>
    /// Represents a status message entry with severity and timestamp.
    /// </summary>
    public sealed class StatusMessageEntry
    {
        public StatusMessageEntry(string message, NotificationType type, DateTime timestamp)
        {
            Message = message;
            Type = type;
            Timestamp = timestamp;
        }

        public string Message { get; }
        public NotificationType Type { get; }
        public DateTime Timestamp { get; }

        public override string ToString()
        {
            return $"[{Timestamp:HH:mm:ss}] {Type}: {Message}";
        }
    }
}
