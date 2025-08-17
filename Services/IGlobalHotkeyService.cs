using System;
using System.Windows.Input;

namespace FFXIManager.Services
{
    /// <summary>
    /// Event arguments for hotkey pressed events
    /// </summary>
    public class HotkeyPressedEventArgs : EventArgs
    {
        public int HotkeyId { get; }
        public ModifierKeys Modifiers { get; }
        public Key Key { get; }

        public HotkeyPressedEventArgs(int hotkeyId, ModifierKeys modifiers, Key key)
        {
            HotkeyId = hotkeyId;
            Modifiers = modifiers;
            Key = key;
        }
    }

    /// <summary>
    /// Service for registering and managing global system hotkeys
    /// </summary>
    public interface IGlobalHotkeyService : IDisposable
    {
        /// <summary>
        /// Fired when a registered hotkey is pressed
        /// </summary>
        event EventHandler<HotkeyPressedEventArgs> HotkeyPressed;

        /// <summary>
        /// Registers a global hotkey with the system
        /// </summary>
        /// <param name="id">Unique identifier for this hotkey</param>
        /// <param name="modifiers">Modifier keys (Ctrl, Alt, Shift, Win)</param>
        /// <param name="key">Primary key</param>
        /// <returns>True if registration succeeded, false otherwise</returns>
        bool RegisterHotkey(int id, ModifierKeys modifiers, Key key);

        /// <summary>
        /// Unregisters a specific hotkey
        /// </summary>
        /// <param name="id">The hotkey ID to unregister</param>
        /// <returns>True if unregistration succeeded, false otherwise</returns>
        bool UnregisterHotkey(int id);

        /// <summary>
        /// Unregisters all hotkeys managed by this service
        /// </summary>
        void UnregisterAll();

        /// <summary>
        /// Gets whether a specific hotkey is currently registered
        /// </summary>
        /// <param name="id">The hotkey ID to check</param>
        /// <returns>True if the hotkey is registered</returns>
        bool IsRegistered(int id);

        /// <summary>
        /// Gets the number of currently registered hotkeys
        /// </summary>
        int RegisteredCount { get; }
    }
}
