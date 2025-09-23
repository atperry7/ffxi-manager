namespace FFXIManager.Models
{
    /// <summary>
    /// Sequential steps in the auto-login process
    /// </summary>
    public enum LoginTaskStep
    {
        /// <summary>
        /// No step currently active
        /// </summary>
        None,

        /// <summary>
        /// 1. Launch Windower application
        /// </summary>
        LaunchWindower,

        /// <summary>
        /// 1.1. Wait for Windower to start (sub-task)
        /// </summary>
        WaitForWindowerStart,

        /// <summary>
        /// 1.2. Verify Windower has loaded (sub-task)
        /// </summary>
        VerifyWindowerLoaded,

        /// <summary>
        /// 1.3. Click the Launch Arrow button (sub-task)
        /// </summary>
        ClickLaunchButton,

        /// <summary>
        /// 2. PlayOnline - Member Selection
        /// </summary>
        MemberSelection,

        /// <summary>
        /// 3. PlayOnline - Password Entry
        /// </summary>
        PasswordEntry,

        /// <summary>
        /// 4. PlayOnline - OTP Entry (if required)
        /// </summary>
        OTPEntry,

        /// <summary>
        /// 5. Final Fantasy XI - Terms Acceptance
        /// </summary>
        TermsAcceptance,

        /// <summary>
        /// 6. Final Fantasy XI - Select Character
        /// </summary>
        CharacterSelection,

        /// <summary>
        /// 7. Final Fantasy XI - Pick Character Slot
        /// </summary>
        CharacterSlotPick,

        /// <summary>
        /// 8. Final Fantasy XI - Confirm Character Login
        /// </summary>
        ConfirmLogin
    }

    /// <summary>
    /// Extension methods for LoginTaskStep enum
    /// </summary>
    public static class LoginTaskStepExtensions
    {
        /// <summary>
        /// Gets the display name for a login task step
        /// </summary>
        public static string GetDisplayName(this LoginTaskStep step) => step switch
        {
            LoginTaskStep.None => "Waiting",
            LoginTaskStep.LaunchWindower => "Launch Windower",
            LoginTaskStep.WaitForWindowerStart => "Wait for Windower to start",
            LoginTaskStep.VerifyWindowerLoaded => "Verify Windower has loaded",
            LoginTaskStep.ClickLaunchButton => "Click the Launch Arrow button",
            LoginTaskStep.MemberSelection => "PlayOnline - Member Selection",
            LoginTaskStep.PasswordEntry => "PlayOnline - Password Entry",
            LoginTaskStep.OTPEntry => "PlayOnline - OTP Entry",
            LoginTaskStep.TermsAcceptance => "Final Fantasy XI - Terms Acceptance",
            LoginTaskStep.CharacterSelection => "Final Fantasy XI - Select Character",
            LoginTaskStep.CharacterSlotPick => "Final Fantasy XI - Pick Character Slot",
            LoginTaskStep.ConfirmLogin => "Final Fantasy XI - Confirm Character Login",
            _ => "Unknown Step"
        };

        /// <summary>
        /// Gets the short display name for a login task step
        /// </summary>
        public static string GetShortDisplayName(this LoginTaskStep step) => step switch
        {
            LoginTaskStep.None => "Waiting",
            LoginTaskStep.LaunchWindower => "Launching Windower",
            LoginTaskStep.WaitForWindowerStart => "Starting Windower",
            LoginTaskStep.VerifyWindowerLoaded => "Verifying Windower",
            LoginTaskStep.ClickLaunchButton => "Clicking Launch",
            LoginTaskStep.MemberSelection => "Selecting Member",
            LoginTaskStep.PasswordEntry => "Entering Password",
            LoginTaskStep.OTPEntry => "Entering OTP",
            LoginTaskStep.TermsAcceptance => "Accepting Terms",
            LoginTaskStep.CharacterSelection => "Selecting Character",
            LoginTaskStep.CharacterSlotPick => "Picking Slot",
            LoginTaskStep.ConfirmLogin => "Confirming Login",
            _ => "Unknown"
        };

        /// <summary>
        /// Gets whether this step is a sub-task of the Windower launch process
        /// </summary>
        public static bool IsWindowerSubTask(this LoginTaskStep step) => step switch
        {
            LoginTaskStep.WaitForWindowerStart => true,
            LoginTaskStep.VerifyWindowerLoaded => true,
            LoginTaskStep.ClickLaunchButton => true,
            _ => false
        };

        /// <summary>
        /// Gets the estimated duration in seconds for this step
        /// </summary>
        public static int GetEstimatedDurationSeconds(this LoginTaskStep step) => step switch
        {
            LoginTaskStep.LaunchWindower => 3,
            LoginTaskStep.WaitForWindowerStart => 5,
            LoginTaskStep.VerifyWindowerLoaded => 2,
            LoginTaskStep.ClickLaunchButton => 1,
            LoginTaskStep.MemberSelection => 3,
            LoginTaskStep.PasswordEntry => 2,
            LoginTaskStep.OTPEntry => 4,
            LoginTaskStep.TermsAcceptance => 2,
            LoginTaskStep.CharacterSelection => 3,
            LoginTaskStep.CharacterSlotPick => 2,
            LoginTaskStep.ConfirmLogin => 3,
            _ => 1
        };

        /// <summary>
        /// Gets all main task steps (excludes sub-tasks and None)
        /// </summary>
        public static IEnumerable<LoginTaskStep> GetMainSteps()
        {
            return new[]
            {
                LoginTaskStep.LaunchWindower,
                LoginTaskStep.MemberSelection,
                LoginTaskStep.PasswordEntry,
                LoginTaskStep.OTPEntry,
                LoginTaskStep.TermsAcceptance,
                LoginTaskStep.CharacterSelection,
                LoginTaskStep.CharacterSlotPick,
                LoginTaskStep.ConfirmLogin
            };
        }

        /// <summary>
        /// Gets all sub-tasks for Windower launch
        /// </summary>
        public static IEnumerable<LoginTaskStep> GetWindowerSubTasks()
        {
            return new[]
            {
                LoginTaskStep.WaitForWindowerStart,
                LoginTaskStep.VerifyWindowerLoaded,
                LoginTaskStep.ClickLaunchButton
            };
        }
    }
}