using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FFXIManager.Models.Settings;
using FFXIManager.Services;

namespace FFXIManager.Controls
{
    /// <summary>
    /// Interactive control for recording keyboard shortcuts with live feedback.
    /// Provides thread-safe resource management and proper disposal.
    /// </summary>
    public sealed partial class KeyRecorderControl : UserControl, IDisposable
    {
        /// <summary>
        /// Temporary hotkey ID used only during shortcut recording.
        /// 
        /// Rationale:
        /// - Uses Int32.MaxValue (0x7FFFFFFF) to avoid conflicts with real hotkey IDs, which are typically assigned sequentially
        ///   or within a lower range.
        /// - This value is reserved for temporary use and is never assigned to actual shortcuts.
        /// - Using the maximum possible int value ensures it does not overlap with any valid hotkey ID in the application or system.
        /// - Prevents accidental reuse or collision, and ensures consistent cleanup of temporary hooks.
        /// </summary>
        public const int TempRecordingHotkeyId = 0x7FFFFFFF;

        private LowLevelHotkeyService? _tempHookService;
        private ModifierKeys _currentModifiers = ModifierKeys.None;
        private Key _currentKey = Key.None;
        private volatile bool _isRecording;
        private volatile bool _disposed;

        public event EventHandler<KeyboardShortcutConfig>? ShortcutRecorded;

        /// <summary>
        /// Initializes a new instance of the KeyRecorderControl.
        /// </summary>
        public KeyRecorderControl()
        {
            InitializeComponent();
            Reset();
        }

        private void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRecording)
            {
                StopRecording();
            }
            else
            {
                StartRecording();
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            Reset();
        }


        /// <summary>
        /// Starts recording keyboard input.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown if the control has been disposed.</exception>
        private void StartRecording()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(KeyRecorderControl));

            try
            {
                _isRecording = true;

                // Create temporary hook service
                _tempHookService = new LowLevelHotkeyService();
                _tempHookService.HotkeyPressed += OnKeyPressed;

                // Update UI
                RecordButton.Content = "⏹ Stop";
                RecordButton.Background = System.Windows.Media.Brushes.Orange;
                KeyDisplayText.Text = "Recording... Press keys now";
                StatusText.Text = "Press any key combination. Recording will stop automatically.";

                ClearButton.IsEnabled = false;

                // Start capturing ALL keys (register a dummy hotkey to activate the hook)
                _tempHookService.RegisterHotkey(TempRecordingHotkeyId, ModifierKeys.None, Key.None, isTemporary: true);

                this.Focus();
                this.Focusable = true;
            }
            catch (Exception ex)
            {
                // Clean up if initialization failed
                CleanupHotkeyService();
                StatusText.Text = $"Error starting recording: {ex.Message}";
                _isRecording = false;
                throw;
            }
        }

        /// <summary>
        /// Stops recording keyboard input and releases resources.
        /// </summary>
        private void StopRecording()
        {
            _isRecording = false;
            CleanupHotkeyService();

            // Update UI
            RecordButton.Content = "📹 Record";
            RecordButton.Background = System.Windows.Media.Brushes.Green;
            ClearButton.IsEnabled = true;

            if (_currentModifiers != ModifierKeys.None && _currentKey != Key.None)
            {
                StatusText.Text = "Shortcut captured! Use Apply in the dialog to save it.";
            }
            else
            {
                StatusText.Text = "No valid shortcut recorded. Try again.";
            }
        }

        /// <summary>
        /// Safely cleans up the hotkey service resources.
        /// </summary>
        private void CleanupHotkeyService()
        {
            if (_tempHookService != null)
            {
                try
                {
                    _tempHookService.HotkeyPressed -= OnKeyPressed;
                    _tempHookService.Dispose();
                }
                catch (Exception ex)
                {
                    // Log the exception but don't throw during cleanup
                    System.Diagnostics.Debug.WriteLine($"Error disposing hotkey service: {ex.Message}");
                }
                finally
                {
                    _tempHookService = null;
                }
            }
        }

        /// <summary>
        /// Handles hotkey press events during recording.
        /// </summary>
        private void OnKeyPressed(object? sender, HotkeyPressedEventArgs e)
        {
            // Ignore if not recording or disposed
            if (!_isRecording || _disposed) return;

            // This is called for EVERY key press while recording
            Dispatcher.BeginInvoke(() =>
            {
                // Ignore modifier-only keys
                if (IsModifierKey(e.Key))
                {
                    return;
                }

                // Capture the combination
                _currentModifiers = e.Modifiers;
                _currentKey = e.Key;

                // Update display
                if (_currentModifiers == ModifierKeys.None)
                {
                    KeyDisplayText.Text = _currentKey.ToString();
                    StatusText.Text = "Single keys are not recommended. Try using Ctrl, Alt, or Shift.";
                }
                else
                {
                    KeyDisplayText.Text = $"{_currentModifiers}+{_currentKey}";
                    StatusText.Text = "Perfect! This combination looks good.";
                }

                // Auto-stop recording after capturing a key
                StopRecording();

                // Automatically fire the ShortcutRecorded event with the captured combination
                var shortcut = new KeyboardShortcutConfig(0, _currentModifiers, _currentKey);
                ShortcutRecorded?.Invoke(this, shortcut);
            });
        }

        /// <summary>
        /// Determines if the specified key is a modifier key (Ctrl, Alt, Shift, Win).
        /// </summary>
        /// <param name="key">The key to check.</param>
        /// <returns>True if the key is a modifier key.</returns>
        private static bool IsModifierKey(Key key)
        {
            return key == Key.LeftCtrl || key == Key.RightCtrl ||
                   key == Key.LeftAlt || key == Key.RightAlt ||
                   key == Key.LeftShift || key == Key.RightShift ||
                   key == Key.LWin || key == Key.RWin;
        }

        /// <summary>
        /// Resets the control to its initial state.
        /// </summary>
        private void Reset()
        {
            StopRecording();

            _currentModifiers = ModifierKeys.None;
            _currentKey = Key.None;

            KeyDisplayText.Text = "Press key combination...";
            StatusText.Text = "Press the keys you want to use as a shortcut";

            RecordButton.Content = "📹 Record";
            RecordButton.Background = System.Windows.Media.Brushes.Green;
            ClearButton.IsEnabled = true;
        }

        /// <summary>
        /// Sets the current shortcut combination.
        /// </summary>
        /// <param name="modifiers">The modifier keys.</param>
        /// <param name="key">The primary key.</param>
        public void SetShortcut(ModifierKeys modifiers, Key key)
        {
            _currentModifiers = modifiers;
            _currentKey = key;

            if (modifiers == ModifierKeys.None && key == Key.None)
            {
                KeyDisplayText.Text = "Press key combination...";
            }
            else
            {
                KeyDisplayText.Text = modifiers == ModifierKeys.None ?
                    key.ToString() :
                    $"{modifiers}+{key}";
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            StopRecording();
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            this.Unloaded += OnUnloaded;
        }

        /// <summary>
        /// Releases all resources used by the KeyRecorderControl.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            StopRecording();

            // Remove event handlers
            this.Unloaded -= OnUnloaded;

            GC.SuppressFinalize(this);
        }
    }
}
