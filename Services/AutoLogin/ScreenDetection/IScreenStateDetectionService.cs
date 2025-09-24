using System;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services.AutoLogin.ScreenDetection
{
    /// <summary>
    /// High-level service for detecting application screen states
    /// </summary>
    public interface IScreenStateDetectionService
    {
        /// <summary>
        /// Detects the current screen state of an application window
        /// </summary>
        /// <param name="windowHandle">Handle to the application window</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Detected screen state with confidence</returns>
        Task<ScreenState> DetectScreenStateAsync(IntPtr windowHandle, CancellationToken cancellationToken = default);

        /// <summary>
        /// Waits for a specific screen state to appear
        /// </summary>
        /// <param name="expectedStep">Expected login task step</param>
        /// <param name="windowHandle">Handle to the application window</param>
        /// <param name="timeout">Maximum time to wait</param>
        /// <param name="progressCallback">Progress reporting callback</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Detected screen state when found</returns>
        Task<ScreenState> WaitForStateAsync(
            LoginTaskStep expectedStep,
            IntPtr windowHandle,
            TimeSpan timeout,
            Action<int, string>? progressCallback = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Verifies that the current screen matches the expected state
        /// </summary>
        /// <param name="windowHandle">Handle to the application window</param>
        /// <param name="expectedStep">Expected login task step</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if the screen matches the expected state</returns>
        Task<bool> VerifyScreenStateAsync(
            IntPtr windowHandle,
            LoginTaskStep expectedStep,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Detects and returns available actions for the current screen state
        /// </summary>
        /// <param name="windowHandle">Handle to the application window</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Available UI automation actions</returns>
        Task<UIAutomationAction[]> GetAvailableActionsAsync(
            IntPtr windowHandle,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Performs the default action for the current screen state
        /// </summary>
        /// <param name="windowHandle">Handle to the application window</param>
        /// <param name="screenState">Current screen state</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task ExecuteDefaultActionAsync(
            IntPtr windowHandle,
            ScreenState screenState,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Waits for a screen transition to complete
        /// </summary>
        /// <param name="windowHandle">Handle to the application window</param>
        /// <param name="fromStep">Starting step</param>
        /// <param name="toStep">Expected destination step</param>
        /// <param name="timeout">Maximum time to wait</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if transition completed successfully</returns>
        Task<bool> WaitForTransitionAsync(
            IntPtr windowHandle,
            LoginTaskStep fromStep,
            LoginTaskStep toStep,
            TimeSpan timeout,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets confidence threshold for state detection
        /// </summary>
        /// <returns>Current confidence threshold</returns>
        float GetConfidenceThreshold();

        /// <summary>
        /// Sets confidence threshold for state detection
        /// </summary>
        /// <param name="threshold">Confidence threshold (0.0 to 1.0)</param>
        void SetConfidenceThreshold(float threshold);

        /// <summary>
        /// Registers a custom state detector for specific applications
        /// </summary>
        /// <param name="applicationName">Name of the application</param>
        /// <param name="detector">Custom state detector implementation</param>
        void RegisterCustomDetector(string applicationName, ICustomStateDetector detector);
    }

    /// <summary>
    /// Interface for custom state detectors
    /// </summary>
    public interface ICustomStateDetector
    {
        /// <summary>
        /// Detects the current state using custom logic
        /// </summary>
        /// <param name="screenshot">Window screenshot</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Detected screen state</returns>
        Task<ScreenState> DetectStateAsync(WindowScreenshot screenshot, CancellationToken cancellationToken = default);
    }
}