using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FFXIManager.Models.Settings
{
    /// <summary>
    /// Represents a keyboard shortcut configuration for character switching
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
        /// Whether this shortcut is enabled
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        /// <summary>
        /// Display-friendly representation of the key combination
        /// </summary>
        public string DisplayText
        {
            get
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

                parts.Add(Key.ToString());

                return string.Join("+", parts);
            }
        }

        /// <summary>
        /// Display-friendly text for the slot (e.g., "Slot 1", "Slot 2")
        /// </summary>
        public string SlotDisplayText
        {
            get => $"Slot {SlotIndex + 1}";
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
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

            // Update dependent properties
            if (propertyName == nameof(Modifiers) || propertyName == nameof(Key))
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
                   Key == other.Key;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(SlotIndex, Modifiers, Key);
        }

        public override string ToString()
        {
            return $"Slot {SlotIndex + 1}: {DisplayText}" + (IsEnabled ? "" : " (Disabled)");
        }
    }
}
