using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FFXIManager.Models.Settings
{
    /// <summary>
    /// Represents an input shortcut configuration supporting both keyboard and controller inputs for character switching
    /// </summary>
    [Serializable]
    public class KeyboardShortcutConfig : INotifyPropertyChanged
    {
        /// <summary>
        /// Offset used to generate unique hotkey IDs to avoid conflicts with other applications
        /// </summary>
        public const int HotkeyIdOffset = 1000;
        private int _slotIndex;
        private ModifierKeys _modifiers;
        private Key _key;
        private ControllerButton _controllerButton = ControllerButton.None;
        private bool _isEnabled = true;

        /// <summary>
        /// The character slot index this shortcut targets (0-based)
        /// </summary>
        public int SlotIndex
        {
            get => _slotIndex;
            set => SetProperty(ref _slotIndex, value);
        }

        /// <summary>
        /// Modifier keys (Ctrl, Alt, Shift, Win) for the shortcut
        /// </summary>
        public ModifierKeys Modifiers
        {
            get => _modifiers;
            set => SetProperty(ref _modifiers, value);
        }

        /// <summary>
        /// The primary key for the shortcut
        /// </summary>
        public Key Key
        {
            get => _key;
            set => SetProperty(ref _key, value);
        }

        /// <summary>
        /// The controller button for the shortcut (None if no controller input)
        /// </summary>
        public ControllerButton ControllerButton
        {
            get => _controllerButton;
            set => SetProperty(ref _controllerButton, value);
        }

        /// <summary>
        /// Whether this shortcut is enabled
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        /// <summary>
        /// Display-friendly representation of the input combination (keyboard and/or controller)
        /// </summary>
        public string DisplayText
        {
            get
            {
                var keyboardText = GetKeyboardDisplayText();
                var controllerText = GetControllerDisplayText();

                // Show unified format based on what's configured
                return (keyboardText, controllerText) switch
                {
                    ("None", "None") => "None",
                    ("None", var controller) => controller, // Controller only
                    (var keyboard, "None") => keyboard,     // Keyboard only  
                    (var keyboard, var controller) => $"{keyboard} + {controller}" // Both
                };
            }
        }


        /// <summary>
        /// Gets the keyboard portion of the display text (public for logging)
        /// </summary>
        public string GetKeyboardDisplayText()
        {
            if (Key == Key.None) return "None";

            var parts = new System.Collections.Generic.List<string>();

            if (Modifiers.HasFlag(ModifierKeys.Control))
                parts.Add("Ctrl");
            if (Modifiers.HasFlag(ModifierKeys.Alt))
                parts.Add("Alt");
            if (Modifiers.HasFlag(ModifierKeys.Shift))
                parts.Add("Shift");
            if (Modifiers.HasFlag(ModifierKeys.Windows))
                parts.Add("Win");

            parts.Add(GetKeyDisplayName(Key));

            return string.Join("+", parts);
        }

        /// <summary>
        /// Gets the controller portion of the display text (public for logging)
        /// </summary>
        public string GetControllerDisplayText()
        {
            return ControllerButton == ControllerButton.None ? "None" : ControllerButton.GetDescription();
        }
        
        /// <summary>
        /// Gets a user-friendly display name for keys, including extended peripheral keys.
        /// </summary>
        private static string GetKeyDisplayName(Key key)
        {
            // Handle extended function keys that might come from gaming peripherals
            return key switch
            {
                >= Key.F13 and <= Key.F24 => key.ToString(), // F13-F24 support
                Key.None => "None",
                _ => key.ToString()
            };
        }

        /// <summary>
        /// Display-friendly text for the slot (e.g., "Slot 1", "Slot 2")
        /// </summary>
        public string SlotDisplayText
        {
            get => SlotIndex == -1 ? "Cycle Characters" : $"Slot {SlotIndex + 1}";
        }

        /// <summary>
        /// Unique identifier for this shortcut (used for hotkey registration)
        /// </summary>
        public int HotkeyId => SlotIndex + HotkeyIdOffset; // Offset to avoid conflicts with other IDs

        /// <summary>
        /// Converts a hotkey ID back to its corresponding slot index
        /// </summary>
        /// <param name="hotkeyId">The hotkey ID to convert</param>
        /// <returns>The 0-based slot index</returns>
        public static int GetSlotIndexFromHotkeyId(int hotkeyId) => hotkeyId - HotkeyIdOffset;

        public KeyboardShortcutConfig()
        {
        }

        public KeyboardShortcutConfig(int slotIndex, ModifierKeys modifiers, Key key)
        {
            SlotIndex = slotIndex;
            Modifiers = modifiers;
            Key = key;
            ControllerButton = ControllerButton.None;
        }

        public KeyboardShortcutConfig(int slotIndex, ModifierKeys modifiers, Key key, ControllerButton controllerButton)
        {
            SlotIndex = slotIndex;
            Modifiers = modifiers;
            Key = key;
            ControllerButton = controllerButton;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

            // Update dependent properties
            if (propertyName == nameof(Modifiers) || propertyName == nameof(Key) || propertyName == nameof(ControllerButton))
            {
                OnPropertyChanged(nameof(DisplayText));
            }
            if (propertyName == nameof(SlotIndex))
            {
                OnPropertyChanged(nameof(SlotDisplayText));
            }
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        public override bool Equals(object? obj)
        {
            if (obj is not KeyboardShortcutConfig other) return false;
            return SlotIndex == other.SlotIndex &&
                   Modifiers == other.Modifiers &&
                   Key == other.Key &&
                   ControllerButton == other.ControllerButton;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(SlotIndex, Modifiers, Key, ControllerButton);
        }

        public override string ToString()
        {
            return $"Slot {SlotIndex + 1}: {DisplayText}" + (IsEnabled ? "" : " (Disabled)");
        }
    }
}
