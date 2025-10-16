using System;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Service for discovering and validating FFXI game processes and window handles.
    /// Handles the transition from PlayOnline to FFXI process with comprehensive discovery logic.
    /// </summary>
    /// <remarks>
    /// <para><strong>Primary Responsibility:</strong></para>
    /// Manages FFXI process detection, window handle discovery, and window responsiveness validation.
    /// Handles the complex transition from PlayOnline process to FFXI game process, which can occur
    /// in multiple ways depending on the launch method and configuration.
    ///
    /// <para><strong>Key Features:</strong></para>
    /// <list type="bullet">
    ///   <item><description>PID-based process transition tracking (PlayOnline → FFXI)</description></item>
    ///   <item><description>Fallback process discovery using process name and window title patterns</description></item>
    ///   <item><description>Window handle validation through screenshot capture</description></item>
    ///   <item><description>Window responsiveness checking for DirectX initialization</description></item>
    ///   <item><description>Context integration for cross-handler window handle management</description></item>
    /// </list>
    ///
    /// <para><strong>FFXI Process Discovery Strategies:</strong></para>
    /// <list type="number">
    ///   <item><description>Primary: Monitor PlayOnline PID for new FFXI window creation</description></item>
    ///   <item><description>Secondary: Check if existing PlayOnline window handle transitioned to FFXI</description></item>
    ///   <item><description>Fallback: Traditional process enumeration with window title matching</description></item>
    ///   <item><description>Validation: Screenshot capture to verify window is responsive</description></item>
    /// </list>
    ///
    /// <para><strong>FFXI-Specific Behavior:</strong></para>
    /// <list type="bullet">
    ///   <item><description>Process may start as 'pol' and transition to 'ffximain' during launch</description></item>
    ///   <item><description>Window handle from PlayOnline phase may become the FFXI window</description></item>
    ///   <item><description>Multiple process names supported (pol, ffxi, ffximain, PlayOnlineViewer)</description></item>
    ///   <item><description>Window title validation supports various FFXI window states and versions</description></item>
    ///   <item><description>DirectX window requires time to become responsive for automation</description></item>
    /// </list>
    ///
    /// <para><strong>Configuration Dependencies:</strong></para>
    /// <list type="bullet">
    ///   <item><description>FFXIGameConfiguration.ProcessDiscovery.ProcessDetectionAttempts</description></item>
    ///   <item><description>FFXIGameConfiguration.ProcessDiscovery.WindowTitlePatterns</description></item>
    ///   <item><description>FFXIGameConfiguration.ProcessDiscovery.ResponsivenessCheckAttempts</description></item>
    ///   <item><description>FFXIGameConfiguration.PollingIntervals.ProcessCheck</description></item>
    /// </list>
    ///
    /// <para><strong>Usage Example:</strong></para>
    /// <code>
    /// // Wait for FFXI process and get window handle
    /// var ffxiHandle = await _processDiscoveryService.WaitForFFXIProcessAsync(
    ///     subtask,
    ///     context,
    ///     cancellationToken);
    ///
    /// // Store handle in context for subsequent handlers
    /// context.SetData("FFXIWindowHandle", ffxiHandle);
    ///
    /// // Validate window is responsive for automation
    /// await _processDiscoveryService.WaitForFFXIStartupAsync(
    ///     ffxiHandle,
    ///     subtask,
    ///     cancellationToken);
    ///
    /// // Later: Retrieve and validate existing handle
    /// var validatedHandle = await _processDiscoveryService.GetFFXIWindowHandleAsync(
    ///     subtask,
    ///     context,
    ///     cancellationToken);
    /// </code>
    /// </remarks>
    public interface IFFXIProcessDiscoveryService
    {
        /// <summary>
        /// Waits for the FFXI process to launch and returns its window handle.
        /// Handles the transition from PlayOnline to FFXI process with comprehensive discovery logic.
        /// </summary>
        /// <param name="subtask">Subtask for progress reporting during process detection</param>
        /// <param name="context">Context for checking stored window handles from previous handlers</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Valid FFXI window handle</returns>
        /// <remarks>
        /// <para><strong>Discovery Flow:</strong></para>
        /// <list type="number">
        ///   <item><description>Check context for PlayOnline PID and window handle</description></item>
        ///   <item><description>Monitor PlayOnline PID for new FFXI window creation (primary method)</description></item>
        ///   <item><description>Check window title patterns against FFXI title patterns</description></item>
        ///   <item><description>Fall back to traditional process enumeration if PID tracking fails</description></item>
        ///   <item><description>Return first valid FFXI window handle found</description></item>
        /// </list>
        ///
        /// <para><strong>Progress Reporting:</strong></para>
        /// <list type="bullet">
        ///   <item><description>10-40%: Monitoring game transition (PID tracking phase)</description></item>
        ///   <item><description>45%: FFXI window transition detected</description></item>
        ///   <item><description>Uses monotonic progress within each phase</description></item>
        /// </list>
        /// </remarks>
        /// <exception cref="InvalidOperationException">FFXI process fails to launch within the configured timeout period</exception>
        /// <exception cref="OperationCanceledException">Operation cancelled via cancellation token</exception>
        Task<IntPtr> WaitForFFXIProcessAsync(
            AutoLoginSubtask subtask,
            IAutoLoginContext context,
            CancellationToken cancellationToken);

        /// <summary>
        /// Waits for FFXI to fully initialize and become responsive for UI automation.
        /// Validates window responsiveness through screenshot capture rather than template matching.
        /// </summary>
        /// <param name="windowHandle">FFXI window handle to validate</param>
        /// <param name="subtask">Subtask for progress reporting during initialization checks</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <remarks>
        /// <para><strong>Responsiveness Validation:</strong></para>
        /// <list type="bullet">
        ///   <item><description>Captures screenshots from provided window handle</description></item>
        ///   <item><description>Validates screenshot is valid (not null, has dimensions)</description></item>
        ///   <item><description>Requires multiple consecutive successful captures before considering ready</description></item>
        ///   <item><description>Applies final stabilization delay for DirectX rendering</description></item>
        /// </list>
        ///
        /// <para><strong>FFXI-Specific:</strong></para>
        /// DirectX window may take time to become responsive for automation. Multiple successful
        /// screenshots are required to ensure window stability. Single successful capture may occur
        /// during initialization but window may still be unstable.
        ///
        /// <para><strong>Progress Reporting:</strong></para>
        /// <list type="bullet">
        ///   <item><description>50-65%: Checking FFXI responsiveness</description></item>
        ///   <item><description>Incremental progress based on number of successful checks</description></item>
        /// </list>
        /// </remarks>
        /// <exception cref="OperationCanceledException">Operation cancelled via cancellation token</exception>
        Task WaitForFFXIStartupAsync(
            IntPtr windowHandle,
            AutoLoginSubtask subtask,
            CancellationToken cancellationToken);

        /// <summary>
        /// Gets FFXI window handle from context or discovers it if not found/invalid.
        /// Provides automatic handle validation and redetection when needed.
        /// </summary>
        /// <param name="subtask">Subtask for progress reporting if redetection is required</param>
        /// <param name="context">Context to retrieve window handle from</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Valid FFXI window handle</returns>
        /// <remarks>
        /// <para><strong>Retrieval Strategy:</strong></para>
        /// <list type="number">
        ///   <item><description>Attempt to retrieve "FFXIWindowHandle" from context</description></item>
        ///   <item><description>If found, validate handle is still valid via screenshot capture</description></item>
        ///   <item><description>If not found or invalid, trigger WaitForFFXIProcessAsync for rediscovery</description></item>
        ///   <item><description>Store newly discovered handle in context</description></item>
        ///   <item><description>Return validated handle</description></item>
        /// </list>
        ///
        /// This method is used by subsequent handler phases (CharacterSelection, CharacterSlotPick, etc.)
        /// to retrieve the FFXI window handle that was discovered during TermsAcceptance phase.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Window handle cannot be found or validated</exception>
        /// <exception cref="OperationCanceledException">Operation cancelled via cancellation token</exception>
        Task<IntPtr> GetFFXIWindowHandleAsync(
            AutoLoginSubtask subtask,
            IAutoLoginContext context,
            CancellationToken cancellationToken);
    }
}
