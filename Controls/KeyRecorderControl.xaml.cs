using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FFXIManager.Models.Settings;
using FFXIManager.Services;

namespace FFXIManager.Controls
{
    /// <summary>
    /// Interactive control for recording keyboard shortcuts with live feedback
    /// </summary>
    public partial class KeyRecorderControl : UserControl
    {
        private LowLevelHotkeyService? _tempHookService;
        private ModifierKeys _currentModifiers = ModifierKeys.None;
        private Key _currentKey = Key.None;
        private bool _isRecording = false;

        public event EventHandler<KeyboardShortcutConfig>? ShortcutRecorded;

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

        private void AcceptButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentModifiers != ModifierKeys.None && _currentKey != Key.None)
            {
                var shortcut = new KeyboardShortcutConfig(0, _currentModifiers, _currentKey);
                ShortcutRecorded?.Invoke(this, shortcut);
                Reset();
            }
        }

        private void StartRecording()
        {
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
                AcceptButton.IsEnabled = false;
                
                // Start capturing ALL keys (register a dummy hotkey to activate the hook)
                _tempHookService.RegisterHotkey(99999, ModifierKeys.None, Key.None);
                
                this.Focus();
                this.Focusable = true;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error starting recording: {ex.Message}";
                StopRecording();
            }
        }

        private void StopRecording()
        {
            _isRecording = false;
            
            // Cleanup hook service
            if (_tempHookService != null)
            {
                _tempHookService.HotkeyPressed -= OnKeyPressed;
                _tempHookService.Dispose();
                _tempHookService = null;
            }
            
            // Update UI
            RecordButton.Content = "📹 Record";
            RecordButton.Background = System.Windows.Media.Brushes.Green;
            ClearButton.IsEnabled = true;
            
            if (_currentModifiers != ModifierKeys.None && _currentKey != Key.None)
            {
                StatusText.Text = "Shortcut captured! Click Accept to use it.";
                AcceptButton.IsEnabled = true;
            }
            else
            {
                StatusText.Text = "No valid shortcut recorded. Try again.";
                AcceptButton.IsEnabled = false;
            }
        }

        private void OnKeyPressed(object? sender, HotkeyPressedEventArgs e)
        {
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
            });
        }

        private static bool IsModifierKey(Key key)
        {
            return key == Key.LeftCtrl || key == Key.RightCtrl ||
                   key == Key.LeftAlt || key == Key.RightAlt ||
                   key == Key.LeftShift || key == Key.RightShift ||
                   key == Key.LWin || key == Key.RWin;
        }

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
            AcceptButton.IsEnabled = false;
        }

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
                AcceptButton.IsEnabled = true;
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
    }
}
