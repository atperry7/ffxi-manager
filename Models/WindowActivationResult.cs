using System;

namespace FFXIManager.Models
{
    /// <summary>
    /// Detailed result of a window activation attempt with diagnostic information.
    /// </summary>
    public class WindowActivationResult
    {
        public bool Success { get; set; }
        public IntPtr WindowHandle { get; set; }
        public WindowActivationFailureReason FailureReason { get; set; }
        public TimeSpan Duration { get; set; }
        public int AttemptsRequired { get; set; }
        public string? DiagnosticInfo { get; set; }
        
        /// <summary>
        /// Window state after activation attempt.
        /// </summary>
        public WindowStateInfo? WindowState { get; set; }
        
        public static WindowActivationResult Successful(IntPtr handle, TimeSpan duration, int attempts = 1) => new()
        {
            Success = true,
            WindowHandle = handle,
            Duration = duration,
            AttemptsRequired = attempts,
            FailureReason = WindowActivationFailureReason.None
        };
        
        public static WindowActivationResult Failed(IntPtr handle, WindowActivationFailureReason reason, string? diagnostic = null) => new()
        {
            Success = false,
            WindowHandle = handle,
            FailureReason = reason,
            DiagnosticInfo = diagnostic
        };
    }
    
    /// <summary>
    /// Specific reasons for window activation failure.
    /// </summary>
    public enum WindowActivationFailureReason
    {
        None = 0,
        InvalidHandle,
        WindowDestroyed,
        WindowHung,
        AccessDenied,
        ElevationMismatch,
        FocusStealingPrevention,
        FullScreenBlocking,
        ThreadAttachmentFailed,
        Timeout,
        Unknown
    }
    
    /// <summary>
    /// Detailed window state information for diagnostics.
    /// </summary>
    public class WindowStateInfo
    {
        public bool IsVisible { get; set; }
        public bool IsMinimized { get; set; }
        public bool IsMaximized { get; set; }
        public bool IsForeground { get; set; }
        public bool IsResponding { get; set; }
        public bool IsTopMost { get; set; }
        public int ZOrder { get; set; }
        public string? WindowTitle { get; set; }
        public string? ClassName { get; set; }
        
        public override string ToString()
        {
            return $"Visible:{IsVisible}, Minimized:{IsMinimized}, Foreground:{IsForeground}, Responding:{IsResponding}";
        }
    }
}