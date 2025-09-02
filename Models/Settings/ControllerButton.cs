using System;
using System.ComponentModel;

namespace FFXIManager.Models.Settings
{
    /// <summary>
    /// Enumeration of supported controller buttons for hotkey assignments.
    /// Focuses on commonly used gaming buttons for FFXI character switching.
    /// </summary>
    [Serializable]
    public enum ControllerButton
    {
        /// <summary>
        /// No controller button assigned
        /// </summary>
        [Description("None")]
        None = 0,

        // D-Pad buttons - Primary navigation
        [Description("D-Pad Up")]
        DPadUp = 1,

        [Description("D-Pad Down")]
        DPadDown = 2,

        [Description("D-Pad Left")]
        DPadLeft = 3,

        [Description("D-Pad Right")]
        DPadRight = 4,

        // Face buttons - Secondary actions
        [Description("A Button")]
        FaceButtonA = 5,

        [Description("B Button")]
        FaceButtonB = 6,

        [Description("X Button")]
        FaceButtonX = 7,

        [Description("Y Button")]
        FaceButtonY = 8,

        // Shoulder buttons - Advanced combinations
        [Description("Left Bumper")]
        LeftBumper = 9,

        [Description("Right Bumper")]
        RightBumper = 10,

        [Description("Left Trigger")]
        LeftTrigger = 11,

        [Description("Right Trigger")]
        RightTrigger = 12,

        // System buttons - Special functions
        [Description("Start")]
        Start = 13,

        [Description("Select/Back")]
        Select = 14,

        // Thumbstick clicks - Advanced users
        [Description("Left Stick Click")]
        LeftThumbstickClick = 15,

        [Description("Right Stick Click")]
        RightThumbstickClick = 16
    }

    /// <summary>
    /// Extension methods for ControllerButton enum
    /// </summary>
    public static class ControllerButtonExtensions
    {
        /// <summary>
        /// Gets the display-friendly description for a controller button
        /// </summary>
        public static string GetDescription(this ControllerButton button)
        {
            var field = button.GetType().GetField(button.ToString());
            var attribute = field?.GetCustomAttributes(typeof(DescriptionAttribute), false) as DescriptionAttribute[];
            return attribute?.Length > 0 ? attribute[0].Description : button.ToString();
        }

        /// <summary>
        /// Determines if this controller button is considered a "primary" button for hotkeys.
        /// Primary buttons are D-Pad and face buttons, which are most commonly used.
        /// </summary>
        public static bool IsPrimaryButton(this ControllerButton button)
        {
            return button switch
            {
                ControllerButton.DPadUp or
                ControllerButton.DPadDown or
                ControllerButton.DPadLeft or
                ControllerButton.DPadRight or
                ControllerButton.FaceButtonA or
                ControllerButton.FaceButtonB or
                ControllerButton.FaceButtonX or
                ControllerButton.FaceButtonY => true,
                _ => false
            };
        }
    }
}