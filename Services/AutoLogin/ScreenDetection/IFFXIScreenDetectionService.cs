using System;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Services.AutoLogin.Configuration;

namespace FFXIManager.Services.AutoLogin.ScreenDetection
{
    /// <summary>
    /// Service for FFXI-specific screen detection with automatic window handle redetection on failure.
    /// Handles the common FFXI issue where window handles become invalid during screen transitions.
    /// </summary>
    /// <remarks>
    /// <para><strong>Primary Responsibility:</strong></para>
    /// Provides robust screen detection with automatic fallback to window handle redetection
    /// when standard template matching fails. This addresses FFXI's tendency to invalidate
    /// window handles during resolution changes, fullscreen transitions, and screen state changes.
    ///
    /// <para><strong>Key Features:</strong></para>
    /// <list type="bullet">
    ///   <item><description>Template-based screen detection with confidence threshold validation</description></item>
    ///   <item><description>Automatic window handle redetection on consecutive screenshot failures</description></item>
    ///   <item><description>Fallback detection strategy (standard detection → redetection)</description></item>
    ///   <item><description>Context integration for window handle updates across handler phases</description></item>
    ///   <item><description>Progress reporting during detection attempts</description></item>
    /// </list>
    ///
    /// <para><strong>FFXI-Specific Behavior:</strong></para>
    /// <list type="bullet">
    ///   <item><description>Window handles frequently become invalid during FFXI screen transitions</description></item>
    ///   <item><description>Resolution changes and fullscreen toggles require handle redetection</description></item>
    ///   <item><description>DirectX rendering can cause intermittent screenshot capture failures</description></item>
    ///   <item><description>Template confidence thresholds loaded from JSON metadata per screen type</description></item>
    /// </list>
    ///
    /// <para><strong>Configuration Dependencies:</strong></para>
    /// <list type="bullet">
    ///   <item><description>FFXIGameConfiguration.ProcessDiscovery.MaxConsecutiveFailures - Triggers redetection</description></item>
    ///   <item><description>Template JSON metadata - Defines confidence thresholds per screen</description></item>
    ///   <item><description>ScreenDetectionOptions - Configures timeout and polling intervals</description></item>
    /// </list>
    ///
    /// <para><strong>Usage Example:</strong></para>
    /// <code>
    /// // Standard detection with automatic redetection fallback
    /// var termsMatch = await _screenDetectionService.WaitForScreenWithRedetectionFallbackAsync(
    ///     subtask,
    ///     FFXIGameConfiguration.TemplatePaths.TermsAcceptance,
    ///     ffxiWindowHandle,
    ///     context,
    ///     "FFXI Terms of Service",
    ///     FFXIGameConfiguration.Timeouts.TermsAcceptance,
    ///     FFXIGameConfiguration.PollingIntervals.ScreenCheck,
    ///     cancellationToken);
    ///
    /// // Enhanced detection with explicit redetection (used when standard method already failed)
    /// var confirmMatch = await _screenDetectionService.WaitForScreenDetectionWithRedetectionAsync(
    ///     subtask,
    ///     FFXIGameConfiguration.TemplatePaths.CharacterConfirmation,
    ///     ffxiWindowHandle,
    ///     context,
    ///     "character confirmation screen",
    ///     cancellationToken,
    ///     new ScreenDetectionOptions
    ///     {
    ///         Timeout = FFXIGameConfiguration.Timeouts.CharacterConfirmation,
    ///         CheckInterval = FFXIGameConfiguration.PollingIntervals.MenuCheck
    ///     });
    /// </code>
    /// </remarks>
    public interface IFFXIScreenDetectionService
    {
        /// <summary>
        /// Performs screen detection with fallback to window redetection on timeout.
        /// This is the primary method for FFXI screen detection - use this for all standard detection scenarios.
        /// </summary>
        /// <param name="subtask">Subtask for progress reporting during detection attempts</param>
        /// <param name="templatePath">Template path for screen detection (e.g., "FFXI/ffxi_terms_acceptance")</param>
        /// <param name="windowHandle">Current FFXI window handle to capture from</param>
        /// <param name="context">Context for window handle updates if redetection occurs</param>
        /// <param name="screenDescription">Human-readable description of screen being detected (for logging)</param>
        /// <param name="timeout">Maximum time to spend on detection attempts</param>
        /// <param name="checkInterval">Time between individual detection attempts</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Template match result with confidence score</returns>
        /// <remarks>
        /// <para><strong>Detection Strategy:</strong></para>
        /// <list type="number">
        ///   <item><description>Primary: Standard screen detection with retry logic</description></item>
        ///   <item><description>Fallback: On TimeoutException, attempt detection with window redetection</description></item>
        /// </list>
        ///
        /// This method centralizes the two-phase detection pattern used throughout FFXIGameHandler:
        /// try standard detection first, fall back to redetection if timeout occurs.
        /// </remarks>
        /// <exception cref="TimeoutException">Both standard and redetection attempts fail within timeout</exception>
        /// <exception cref="OperationCanceledException">Operation cancelled via cancellation token</exception>
        Task<TemplateMatchResult> WaitForScreenWithRedetectionFallbackAsync(
            AutoLoginSubtask subtask,
            string templatePath,
            IntPtr windowHandle,
            IAutoLoginContext context,
            string screenDescription,
            TimeSpan timeout,
            TimeSpan checkInterval,
            CancellationToken cancellationToken);

        /// <summary>
        /// Screen detection with automatic window handle redetection when screenshots fail.
        /// This handles the common FFXI issue where window handles become invalid during transitions.
        /// Use this method when you need explicit redetection logic (e.g., as a fallback in WaitForScreenWithRedetectionFallbackAsync).
        /// </summary>
        /// <param name="subtask">Subtask for progress reporting</param>
        /// <param name="templatePath">Path to the template for detection</param>
        /// <param name="initialWindowHandle">Initial window handle to use</param>
        /// <param name="context">Context for storing window handle updates</param>
        /// <param name="screenDescription">Description of the screen being detected</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <param name="options">Detection options (timeout, intervals, etc.)</param>
        /// <returns>Template match result</returns>
        /// <remarks>
        /// <para><strong>Redetection Logic:</strong></para>
        /// <list type="bullet">
        ///   <item><description>Monitors consecutive screenshot failures</description></item>
        ///   <item><description>Triggers window redetection after MaxConsecutiveFailures threshold</description></item>
        ///   <item><description>Updates context with new window handle if redetection succeeds</description></item>
        ///   <item><description>Resets failure counter after successful redetection</description></item>
        ///   <item><description>Continues detection with updated handle</description></item>
        /// </list>
        ///
        /// <para><strong>FFXI-Specific:</strong></para>
        /// Window handles become invalid during:
        /// - Screen transitions (main menu → character selection)
        /// - Resolution changes
        /// - Fullscreen mode toggles
        /// - DirectX rendering state changes
        /// </remarks>
        /// <exception cref="InvalidOperationException">Template metadata not found</exception>
        /// <exception cref="TimeoutException">Detection fails within configured timeout</exception>
        /// <exception cref="OperationCanceledException">Operation cancelled via cancellation token</exception>
        Task<TemplateMatchResult> WaitForScreenDetectionWithRedetectionAsync(
            AutoLoginSubtask subtask,
            string templatePath,
            IntPtr initialWindowHandle,
            IAutoLoginContext context,
            string screenDescription,
            CancellationToken cancellationToken,
            ScreenDetectionOptions? options = null);
    }
}
