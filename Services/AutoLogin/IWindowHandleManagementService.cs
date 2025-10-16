using System;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Generic service for window handle management and validation operations.
    /// Provides reusable window handle operations across different auto-login handlers.
    /// </summary>
    /// <remarks>
    /// <para><strong>Primary Responsibility:</strong></para>
    /// Centralizes common window handle operations including validation, context management,
    /// focus preparation, and window state checking. Designed to be handler-agnostic and
    /// reusable across all auto-login handlers (FFXI, Windower, POL Proxy, etc.).
    ///
    /// <para><strong>Key Features:</strong></para>
    /// <list type="bullet">
    ///   <item><description>Window handle validation through screenshot capture</description></item>
    ///   <item><description>Context-based handle retrieval with fallback support</description></item>
    ///   <item><description>Window focus preparation with stabilization delays</description></item>
    ///   <item><description>Window state checking and responsiveness validation</description></item>
    /// </list>
    ///
    /// <para><strong>Design Philosophy:</strong></para>
    /// This service is intentionally generic to support reuse across multiple handlers.
    /// Handler-specific logic (e.g., FFXI process discovery) belongs in specialized services.
    /// This service focuses on common window management patterns that apply to any window handle.
    ///
    /// <para><strong>Common Use Cases:</strong></para>
    /// <list type="bullet">
    ///   <item><description>Validating window handles retrieved from context</description></item>
    ///   <item><description>Preparing windows for keyboard/mouse input (focus + stabilization)</description></item>
    ///   <item><description>Checking if window handles are still valid during long operations</description></item>
    ///   <item><description>Managing window handle transitions between handler phases</description></item>
    /// </list>
    ///
    /// <para><strong>DirectX-Specific Considerations:</strong></para>
    /// Many games use DirectX rendering which has specific requirements:
    /// <list type="bullet">
    ///   <item><description>Explicit window focus required before input delivery</description></item>
    ///   <item><description>Stabilization delays needed after focus changes</description></item>
    ///   <item><description>Window handles can become invalid during rendering state changes</description></item>
    /// </list>
    ///
    /// <para><strong>Usage Example:</strong></para>
    /// <code>
    /// // Validate and retrieve window handle from context
    /// var windowHandle = await _windowHandleService.ValidateWindowContextAsync(
    ///     context,
    ///     "FFXIWindowHandle",
    ///     fallbackHandle);
    ///
    /// // Prepare window for keyboard input
    /// await _windowHandleService.PrepareWindowForNavigationAsync(
    ///     windowHandle,
    ///     FFXIGameConfiguration.Delays.WindowFocus,
    ///     cancellationToken);
    ///
    /// // Check if handle is still valid
    /// var isValid = await _windowHandleService.IsWindowHandleValidAsync(
    ///     windowHandle,
    ///     cancellationToken);
    /// </code>
    /// </remarks>
    public interface IWindowHandleManagementService
    {
        /// <summary>
        /// Validates and retrieves a window handle from context with fallback support.
        /// Returns the context handle if valid, otherwise returns the provided fallback handle.
        /// </summary>
        /// <param name="context">Context to retrieve window handle from</param>
        /// <param name="contextKey">Context key for the window handle (e.g., "FFXIWindowHandle")</param>
        /// <param name="fallbackHandle">Fallback handle if context handle is invalid or not found</param>
        /// <returns>Valid window handle (from context or fallback)</returns>
        /// <remarks>
        /// <para><strong>Validation Logic:</strong></para>
        /// <list type="number">
        ///   <item><description>Attempts to retrieve handle from context using provided key</description></item>
        ///   <item><description>Checks if retrieved handle is non-zero (valid)</description></item>
        ///   <item><description>Returns context handle if valid, fallback handle otherwise</description></item>
        /// </list>
        ///
        /// This method provides a centralized pattern for window handle management throughout handlers.
        /// It eliminates duplicate validation logic and ensures consistent handle retrieval behavior.
        ///
        /// <para><strong>Common Usage Pattern:</strong></para>
        /// After screen detection methods that may trigger window redetection, use this method
        /// to get the potentially updated window handle from context.
        /// </remarks>
        IntPtr ValidateWindowContextAsync(
            IAutoLoginContext context,
            string contextKey,
            IntPtr fallbackHandle = default);

        /// <summary>
        /// Prepares a window for navigation input by ensuring focus and applying stabilization delay.
        /// Critical for DirectX window input reliability.
        /// </summary>
        /// <param name="windowHandle">Handle of the window to prepare for input</param>
        /// <param name="delayType">Configured delay for focus stabilization</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <remarks>
        /// <para><strong>Preparation Steps:</strong></para>
        /// <list type="number">
        ///   <item><description>Brings window to foreground and sets input focus</description></item>
        ///   <item><description>Applies configured stabilization delay</description></item>
        /// </list>
        ///
        /// <para><strong>DirectX Requirements:</strong></para>
        /// DirectX applications require explicit focus before accepting input reliably.
        /// Focus changes need processing time before input delivery (timing-sensitive).
        /// Window focus can be lost between navigation steps, especially in windowed mode.
        /// Stabilization delay prevents input timing issues with DirectX UI processing.
        ///
        /// <para><strong>Common Delay Types:</strong></para>
        /// <list type="bullet">
        ///   <item><description>WindowFocus (500ms): Standard focus preparation before input</description></item>
        ///   <item><description>FocusStabilization (750ms): Extended stabilization for complex navigation</description></item>
        ///   <item><description>EnterPreparation (750ms): Focus preparation before critical Enter key presses</description></item>
        /// </list>
        /// </remarks>
        /// <exception cref="OperationCanceledException">Operation cancelled via cancellation token</exception>
        Task PrepareWindowForNavigationAsync(
            IntPtr windowHandle,
            TimeSpan delayType,
            CancellationToken cancellationToken);

        /// <summary>
        /// Checks if a window handle is still valid and responsive.
        /// Uses screenshot capture as a validation mechanism.
        /// </summary>
        /// <param name="windowHandle">Window handle to validate</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if window handle is valid and responsive, false otherwise</returns>
        /// <remarks>
        /// <para><strong>Validation Method:</strong></para>
        /// Attempts to capture a screenshot from the window handle. If screenshot capture
        /// succeeds and produces a valid image, the window handle is considered valid.
        ///
        /// This is more reliable than simple handle existence checks because it verifies
        /// the window is actually responsive and can be automated.
        ///
        /// <para><strong>Common Scenarios:</strong></para>
        /// <list type="bullet">
        ///   <item><description>Validating handles before long operations</description></item>
        ///   <item><description>Checking if redetection is needed after failures</description></item>
        ///   <item><description>Verifying handles retrieved from context are still valid</description></item>
        /// </list>
        /// </remarks>
        Task<bool> IsWindowHandleValidAsync(
            IntPtr windowHandle,
            CancellationToken cancellationToken);
    }
}
