using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FFXIManager.Models.Settings;
using FFXIManager.Services;

namespace FFXIManager.Controls
{
    /// <summary>
    /// Interactive control for recording keyboard and controller input shortcuts with live feedback.
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
        private ControllerInputService? _tempControllerService;
        private ModifierKeys _currentModifiers = ModifierKeys.None;
        private Key _currentKey = Key.None;
        private ControllerButton _currentControllerButton = ControllerButton.None;
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
        /// Starts recording keyboard and controller input.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown if the control has been disposed.</exception>
        private void StartRecording()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(KeyRecorderControl));

            try
            {
                _isRecording = true;

                // Create temporary hook service for keyboard
                _tempHookService = new LowLevelHotkeyService();
                _tempHookService.HotkeyPressed += OnKeyPressed;

                // Create temporary controller service
                _tempControllerService = new ControllerInputService();
                _tempControllerService.ButtonPressed += OnControllerButtonPressed;

                // Register ALL controller buttons for temporary recording (like we do for keyboard)
                RegisterAllControllerButtonsForRecording();

                // Update UI
                RecordButton.Content = "⏹ Stop";
                RecordButton.Background = System.Windows.Media.Brushes.Orange;
                KeyDisplayText.Text = "Recording... Press keyboard or controller";
                StatusText.Text = _tempControllerService.IsAnyControllerConnected ? 
                    "Press any keyboard key or controller button. Recording will stop automatically." :
                    "Press any keyboard key. (No controller detected)";

                ClearButton.IsEnabled = false;

                // Start capturing ALL keys (register a dummy hotkey to activate the hook)
                _tempHookService.RegisterHotkey(TempRecordingHotkeyId, ModifierKeys.None, Key.None, isTemporary: true);

                this.Focus();
                this.Focusable = true;
            }
            catch (Exception ex)
            {
                // Clean up if initialization failed
                CleanupInputServices();
                StatusText.Text = $"Error starting recording: {ex.Message}";
                _isRecording = false;
                throw;
            }
        }

        /// <summary>
        /// Stops recording input and releases resources.
        /// </summary>
        private void StopRecording()
        {
            _isRecording = false;
            CleanupInputServices();

            // Update UI
            RecordButton.Content = "📹 Record";
            RecordButton.Background = System.Windows.Media.Brushes.Green;
            ClearButton.IsEnabled = true;

            // Check if we have any valid input recorded
            var hasKeyboard = (_currentModifiers != ModifierKeys.None && _currentKey != Key.None);
            var hasController = (_currentControllerButton != ControllerButton.None);

            if (hasKeyboard || hasController)
            {
                StatusText.Text = "Input captured! Use Apply in the dialog to save it.";
            }
            else
            {
                StatusText.Text = "No valid input recorded. Try again.";
            }
        }

        /// <summary>
        /// Safely cleans up both keyboard and controller service resources.
        /// </summary>
        private void CleanupInputServices()
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

            if (_tempControllerService != null)
            {
                try
                {
                    _tempControllerService.ButtonPressed -= OnControllerButtonPressed;
                    _tempControllerService.Dispose();
                }
                catch (Exception ex)
                {
                    // Log the exception but don't throw during cleanup
                    System.Diagnostics.Debug.WriteLine($"Error disposing controller service: {ex.Message}");
                }
                finally
                {
                    _tempControllerService = null;
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
                UpdateDisplayForCurrentInputs();

                // Auto-stop recording after capturing a key
                StopRecording();

                // Automatically fire the ShortcutRecorded event with the captured combination
                var shortcut = new KeyboardShortcutConfig(0, _currentModifiers, _currentKey, _currentControllerButton);
                ShortcutRecorded?.Invoke(this, shortcut);
            });
        }

        /// <summary>
        /// Handles controller button press events during recording.
        /// </summary>
        private void OnControllerButtonPressed(object? sender, ControllerButtonPressedEventArgs e)
        {
            // Ignore if not recording or disposed
            if (!_isRecording || _disposed) 
            {
                System.Diagnostics.Debug.WriteLine($"🎮 Controller button ignored: Recording={_isRecording}, Disposed={_disposed}");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"🎮 Controller button pressed during recording: {e.Button} (HotkeyId: {e.HotkeyId})");

            Dispatcher.BeginInvoke(() =>
            {
                // Capture the controller button
                _currentControllerButton = e.Button;
                System.Diagnostics.Debug.WriteLine($"🎮 Captured controller button: {_currentControllerButton}");

                // Update display
                UpdateDisplayForCurrentInputs();

                // Auto-stop recording after capturing a button
                StopRecording();

                // Automatically fire the ShortcutRecorded event with the captured combination
                var shortcut = new KeyboardShortcutConfig(0, _currentModifiers, _currentKey, _currentControllerButton);
                System.Diagnostics.Debug.WriteLine($"🎮 Firing ShortcutRecorded event: {shortcut.DisplayText}");
                ShortcutRecorded?.Invoke(this, shortcut);
            });
        }

        /// <summary>
        /// Updates the display text and status based on currently recorded inputs.
        /// </summary>
        private void UpdateDisplayForCurrentInputs()
        {
            var keyboardText = GetKeyboardDisplayText();
            var controllerText = GetControllerDisplayText();

            // Build unified display text
            var displayText = (keyboardText, controllerText) switch
            {
                ("None", "None") => "Press input...",
                ("None", var controller) => controller, // Controller only
                (var keyboard, "None") => keyboard,     // Keyboard only  
                (var keyboard, var controller) => $"{keyboard} + {controller}" // Both
            };

            KeyDisplayText.Text = displayText;

            // Update status based on input quality
            if (_currentModifiers == ModifierKeys.None && _currentKey != Key.None && _currentControllerButton == ControllerButton.None)
            {
                StatusText.Text = "Single keys are not recommended. Try using Ctrl, Alt, or Shift.";
            }
            else if (_currentModifiers != ModifierKeys.None || _currentControllerButton != ControllerButton.None)
            {
                StatusText.Text = "Perfect! This input combination looks good.";
            }
            else
            {
                StatusText.Text = "Press keyboard keys or controller buttons.";
            }
        }

        /// <summary>
        /// Gets the keyboard portion display text for current state.
        /// </summary>
        private string GetKeyboardDisplayText()
        {
            if (_currentKey == Key.None) return "None";

            var parts = new System.Collections.Generic.List<string>();

            if (_currentModifiers.HasFlag(ModifierKeys.Control))
                parts.Add("Ctrl");
            if (_currentModifiers.HasFlag(ModifierKeys.Alt))
                parts.Add("Alt");
            if (_currentModifiers.HasFlag(ModifierKeys.Shift))
                parts.Add("Shift");
            if (_currentModifiers.HasFlag(ModifierKeys.Windows))
                parts.Add("Win");

            parts.Add(_currentKey.ToString());

            return string.Join("+", parts);
        }

        /// <summary>
        /// Gets the controller portion display text for current state.
        /// </summary>
        private string GetControllerDisplayText()
        {
            return _currentControllerButton == ControllerButton.None ? "None" : _currentControllerButton.GetDescription();
        }

        /// <summary>
        /// Registers all controller buttons for temporary recording (similar to how keyboard captures all keys)
        /// </summary>
        private void RegisterAllControllerButtonsForRecording()
        {
            if (_tempControllerService == null) return;

            System.Diagnostics.Debug.WriteLine("🎮 Registering all controller buttons for recording...");

            // Get all controller button values except None
            var allButtons = Enum.GetValues<ControllerButton>()
                .Where(button => button != ControllerButton.None)
                .ToArray();

            foreach (var button in allButtons)
            {
                // Use a unique temporary ID for each button during recording
                var tempId = TempRecordingHotkeyId + (int)button;
                var success = _tempControllerService.RegisterButton(tempId, button);
                
                System.Diagnostics.Debug.WriteLine($"🎮 Registered {button.GetDescription()}: {(success ? "✅" : "❌")}");
            }

            System.Diagnostics.Debug.WriteLine($"🎮 Registered {allButtons.Length} controller buttons for recording");
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
            _currentControllerButton = ControllerButton.None;

            KeyDisplayText.Text = "Press keyboard or controller input...";
            StatusText.Text = "Press keys or controller buttons for your shortcut";

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
            SetShortcut(modifiers, key, ControllerButton.None);
        }

        /// <summary>
        /// Sets the current input shortcut combination (keyboard + controller).
        /// </summary>
        /// <param name="modifiers">The modifier keys.</param>
        /// <param name="key">The primary key.</param>
        /// <param name="controllerButton">The controller button.</param>
        public void SetShortcut(ModifierKeys modifiers, Key key, ControllerButton controllerButton)
        {
            _currentModifiers = modifiers;
            _currentKey = key;
            _currentControllerButton = controllerButton;

            if (modifiers == ModifierKeys.None && key == Key.None && controllerButton == ControllerButton.None)
            {
                KeyDisplayText.Text = "Press keyboard or controller input...";
            }
            else
            {
                UpdateDisplayForCurrentInputs();
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
