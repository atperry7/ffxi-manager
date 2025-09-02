using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FFXIManager.Infrastructure;
using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.ViewModels.Base;

namespace FFXIManager.ViewModels
{
    /// <summary>
    /// ViewModel for displaying real-time activation feedback without blocking UI
    /// </summary>
    public class ActivationFeedbackViewModel : ViewModelBase, IDisposable
    {
        private readonly IHotkeyActivationService _activationService;
        private readonly DispatcherTimer _cleanupTimer;
        private string _lastStatus = string.Empty;
        private double _averageActivationTime;
        private int _successCount;
        private int _failureCount;
        
        public ActivationFeedbackViewModel(IHotkeyActivationService activationService)
        {
            _activationService = activationService ?? throw new ArgumentNullException(nameof(activationService));
            
            RecentActivations = new ObservableCollection<ActivationFeedback>();
            ClearHistoryCommand = new RelayCommand(() => RecentActivations.Clear());
            
            // Subscribe to activation events
            _activationService.CharacterActivated += OnCharacterActivated;
            
            // Setup cleanup timer to remove old notifications
            _cleanupTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            _cleanupTimer.Tick += CleanupOldNotifications;
            _cleanupTimer.Start();
        }
        
        #region Properties
        
        /// <summary>
        /// Recent activation attempts for display in UI
        /// </summary>
        public ObservableCollection<ActivationFeedback> RecentActivations { get; }
        
        /// <summary>
        /// Last activation status for status bar
        /// </summary>
        public string LastStatus
        {
            get => _lastStatus;
            private set => SetProperty(ref _lastStatus, value);
        }
        
        /// <summary>
        /// Average activation time in milliseconds
        /// </summary>
        public double AverageActivationTime
        {
            get => _averageActivationTime;
            private set => SetProperty(ref _averageActivationTime, value);
        }
        
        /// <summary>
        /// Success rate percentage
        /// </summary>
        public double SuccessRate => 
            (_successCount + _failureCount) > 0 
                ? (_successCount * 100.0) / (_successCount + _failureCount) 
                : 100.0;
        
        public ICommand ClearHistoryCommand { get; }
        
        #endregion
        
        #region Event Handlers
        
        private void OnCharacterActivated(object? sender, HotkeyActivationResult result)
        {
            // Update on UI thread
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                // Create feedback item
                var feedback = new ActivationFeedback
                {
                    Timestamp = DateTime.Now,
                    CharacterName = result.Character?.DisplayName ?? "Unknown",
                    Success = result.Success,
                    Duration = result.Duration,
                    ErrorMessage = result.ErrorMessage,
                    Icon = GetIcon(result),
                    Message = GetMessage(result),
                    Severity = GetSeverity(result)
                };
                
                // Add to collection (limit to 10 recent)
                RecentActivations.Insert(0, feedback);
                while (RecentActivations.Count > 10)
                {
                    RecentActivations.RemoveAt(RecentActivations.Count - 1);
                }
                
                // Update statistics
                if (result.Success)
                {
                    _successCount++;
                }
                else
                {
                    _failureCount++;
                }
                
                // Update average time
                var recentTimes = RecentActivations
                    .Where(a => a.Success)
                    .Select(a => a.Duration.TotalMilliseconds)
                    .Take(5);
                    
                if (recentTimes.Any())
                {
                    AverageActivationTime = recentTimes.Average();
                }
                
                // Update status
                LastStatus = feedback.Message;
                OnPropertyChanged(nameof(SuccessRate));
            });
        }
        
        private void CleanupOldNotifications(object? sender, EventArgs e)
        {
            var cutoff = DateTime.Now.AddSeconds(-30);
            var toRemove = RecentActivations
                .Where(a => a.Timestamp < cutoff)
                .ToList();
                
            foreach (var item in toRemove)
            {
                RecentActivations.Remove(item);
            }
        }
        
        #endregion
        
        #region Helpers
        
        private static string GetIcon(HotkeyActivationResult result)
        {
            if (result.Success) return "✅";
            
            // Parse error message to determine icon
            var errorMessage = result.ErrorMessage?.ToLowerInvariant() ?? "";
            
            if (errorMessage.Contains("not responding") || errorMessage.Contains("hung"))
                return "🔴";
            if (errorMessage.Contains("access denied"))
                return "🔒";
            if (errorMessage.Contains("administrator") || errorMessage.Contains("elevation"))
                return "🛡️";
            if (errorMessage.Contains("fullscreen"))
                return "🖥️";
            if (errorMessage.Contains("timeout"))
                return "⏱️";
                
            return "⚠️";
        }
        
        private static string GetMessage(HotkeyActivationResult result)
        {
            if (result.Success)
            {
                return $"{result.Character?.DisplayName} activated in {result.Duration.TotalMilliseconds:F0}ms";
            }
            
            var characterName = result.Character?.DisplayName ?? "Character";
            var errorMessage = result.ErrorMessage ?? "Activation failed";
            
            return $"{characterName}: {errorMessage}";
        }
        
        private static FeedbackSeverity GetSeverity(HotkeyActivationResult result)
        {
            if (result.Success)
            {
                return result.Duration.TotalMilliseconds < 50 
                    ? FeedbackSeverity.Success 
                    : FeedbackSeverity.Info;
            }
            
            // Parse error message to determine severity
            var errorMessage = result.ErrorMessage?.ToLowerInvariant() ?? "";
            
            if (errorMessage.Contains("not responding") || errorMessage.Contains("hung"))
                return FeedbackSeverity.Critical;
            if (errorMessage.Contains("access denied") || errorMessage.Contains("administrator"))
                return FeedbackSeverity.Error;
                
            return FeedbackSeverity.Warning;
        }
        
        #endregion
        
        public void Dispose()
        {
            _cleanupTimer?.Stop();
            if (_activationService != null)
            {
                _activationService.CharacterActivated -= OnCharacterActivated;
            }
            GC.SuppressFinalize(this);
        }
    }
    
    /// <summary>
    /// Represents a single activation feedback item
    /// </summary>
    public class ActivationFeedback
    {
        public DateTime Timestamp { get; init; }
        public string CharacterName { get; init; } = string.Empty;
        public bool Success { get; init; }
        public TimeSpan Duration { get; init; }
        public string? ErrorMessage { get; init; }
        public string Icon { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public FeedbackSeverity Severity { get; init; }
        
        public string TimeAgo
        {
            get
            {
                var elapsed = DateTime.Now - Timestamp;
                if (elapsed.TotalSeconds < 5) return "just now";
                if (elapsed.TotalSeconds < 60) return $"{(int)elapsed.TotalSeconds}s ago";
                return $"{(int)elapsed.TotalMinutes}m ago";
            }
        }
    }
    
    public enum FeedbackSeverity
    {
        Success,
        Info,
        Warning,
        Error,
        Critical
    }
}