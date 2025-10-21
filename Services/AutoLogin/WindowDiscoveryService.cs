using FFXIManager.Infrastructure;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Implementation of window discovery service that centralizes window handle discovery logic.
    /// Leverages existing services for robust, consistent window discovery.
    /// Implements PID-first architecture for improved reliability in Windows 11.
    /// </summary>
    public class WindowDiscoveryService : IWindowDiscoveryService
    {
        private readonly IPlayOnlineMonitorService _polMonitorService;
        private readonly IExternalApplicationService _externalApplicationService;
        private readonly IProcessUtilityService _processUtilityService;
        private readonly ILoggingService _loggingService;

        public WindowDiscoveryService(
            IPlayOnlineMonitorService polMonitorService,
            IExternalApplicationService externalApplicationService,
            IProcessUtilityService processUtilityService,
            ILoggingService loggingService)
        {
            _polMonitorService = polMonitorService ?? throw new ArgumentNullException(nameof(polMonitorService));
            _externalApplicationService = externalApplicationService ?? throw new ArgumentNullException(nameof(externalApplicationService));
            _processUtilityService = processUtilityService ?? throw new ArgumentNullException(nameof(processUtilityService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        public async Task<ProcessWindowInfo> DiscoverPlayOnlineWindowInfoAsync(
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            await _loggingService.LogDebugAsync("[WINDOW-DISCOVERY] Requesting valid POL window from PlayOnlineMonitorService");

            // Extract PID hint from context
            int? preferredProcessId = ExtractPidHintFromContext(context);

            if (preferredProcessId.HasValue)
            {
                await _loggingService.LogInfoAsync($"[WINDOW-DISCOVERY] Using PID hint: {preferredProcessId}");
            }

            // Get window handle from POL monitor service
            var windowHandle = await _polMonitorService.GetValidPlayOnlineWindowAsync(preferredProcessId);

            if (windowHandle == IntPtr.Zero)
            {
                await _loggingService.LogWarningAsync("[WINDOW-DISCOVERY] PlayOnlineMonitorService could not find valid POL window");
                throw new InvalidOperationException("Could not find valid PlayOnline window. Ensure PlayOnline is running.");
            }

            // Get PID from window handle
            var processId = await GetProcessIdFromWindowHandleAsync(windowHandle);

            await _loggingService.LogInfoAsync($"[WINDOW-DISCOVERY] Found POL window - PID: {processId}, Handle: 0x{windowHandle.ToInt64():X}");

            return new ProcessWindowInfo
            {
                ProcessId = processId,
                WindowHandle = windowHandle,
                ApplicationName = "PlayOnline",
                WindowTitle = await GetWindowTitleAsync(windowHandle)
            };
        }

        public async Task<ProcessWindowInfo?> DiscoverApplicationWindowInfoAsync(
            string applicationName,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(applicationName))
            {
                throw new ArgumentException("Application name cannot be null or empty", nameof(applicationName));
            }

            await _loggingService.LogDebugAsync($"[WINDOW-DISCOVERY] Discovering window for application: {applicationName}");

            try
            {
                // Use ExternalApplicationService to find running instances
                var apps = await _externalApplicationService.GetApplicationsAsync();
                var targetApp = apps.FirstOrDefault(x => x.Name.Equals(applicationName, StringComparison.OrdinalIgnoreCase));

                if (targetApp == null || !targetApp.IsRunning)
                {
                    await _loggingService.LogWarningAsync($"[WINDOW-DISCOVERY] Application not running: {applicationName}");
                    return null;
                }

                // Resolve ApplicationId from name if available in context map
                int hintedPid = 0;
                var appId = context.GetValueData<Guid>(AutoLoginContextKeys.ApplicationIdMap(applicationName));
                if (appId != Guid.Empty)
                {
                    hintedPid = context.GetValueData<int>(AutoLoginContextKeys.LaunchProcessIdByApp(appId));
                }

                if (hintedPid == 0)
                {
                    // Legacy: try name/step-based key
                    var hintKey = AutoLoginContextKeys.LaunchProcessId(applicationName);
                    hintedPid = context.GetValueData<int>(hintKey);
                }

                if (hintedPid > 0 && targetApp.ProcessIds.Contains(hintedPid))
                {
                    var info = await GetWindowInfoForProcessAsync(hintedPid, applicationName);
                    if (info != null)
                    {
                        return info;
                    }
                }

                // Fallback: try any PID from the application
                foreach (var pid in targetApp.ProcessIds)
                {
                    var info = await GetWindowInfoForProcessAsync(pid, applicationName);
                    if (info != null)
                    {
                        return info;
                    }
                }

                await _loggingService.LogWarningAsync($"[WINDOW-DISCOVERY] No valid windows found for {applicationName}");
                return null;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"[WINDOW-DISCOVERY] Error discovering window for {applicationName}", ex);
                return null;
            }
        }

        public async Task<ProcessWindowInfo> DiscoverWindowInfoAsync(
            string? applicationName,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            var app = (applicationName ?? string.Empty).Trim();

            // Default to PlayOnline if unspecified
            if (string.IsNullOrEmpty(app))
            {
                return await DiscoverPlayOnlineWindowInfoAsync(context, cancellationToken);
            }

            // Treat any variant of PlayOnline/FFXI as POL route
            var normalized = app.ToLowerInvariant();
            if (normalized.Contains("playonline") || normalized == "pol" || normalized.Contains("ffxi"))
            {
                return await DiscoverPlayOnlineWindowInfoAsync(context, cancellationToken);
            }

            // External application route
            var info = await DiscoverApplicationWindowInfoAsync(app, context, cancellationToken);
            if (info == null)
            {
                throw new InvalidOperationException($"Could not find valid window for application: {app}");
            }

            return info;
        }

        public async Task<IntPtr> GetFreshWindowHandleFromPidAsync(int processId, string? applicationName = null)
        {
            if (processId <= 0)
            {
                await _loggingService.LogWarningAsync("[WINDOW-REFRESH] Invalid process ID provided");
                return IntPtr.Zero;
            }

            await _loggingService.LogDebugAsync($"[WINDOW-REFRESH] Refreshing window handle for PID {processId}");

            if (!_processUtilityService.IsProcessRunning(processId))
            {
                await _loggingService.LogWarningAsync($"[WINDOW-REFRESH] Process {processId} is no longer running");
                return IntPtr.Zero;
            }

            var windows = await _processUtilityService.GetProcessWindowsAsync(processId);

            // Smart window selection: prefer main window over splash screens
            var mainWindow = windows.FirstOrDefault(w => w.IsVisible && w.IsMainWindow)
                          ?? windows.FirstOrDefault(w => w.IsVisible);

            var handle = mainWindow?.Handle ?? IntPtr.Zero;

            if (handle != IntPtr.Zero && _processUtilityService.IsWindowValid(handle))
            {
                await _loggingService.LogDebugAsync(
                    $"[WINDOW-REFRESH] Refreshed window handle for {applicationName ?? "PID " + processId}: " +
                    $"0x{handle.ToInt64():X} (Title: '{mainWindow?.Title ?? "Unknown"}')");
                return handle;
            }

            await _loggingService.LogWarningAsync($"[WINDOW-REFRESH] No valid windows found for PID {processId}");
            return IntPtr.Zero;
        }

        /// <summary>
        /// Legacy method for backward compatibility.
        /// Returns window handle only - prefer using DiscoverWindowInfoAsync for PID-first approach.
        /// </summary>
        public async Task<IntPtr> DiscoverWindowAsync(
            string? applicationName,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            var info = await DiscoverWindowInfoAsync(applicationName, context, cancellationToken);
            return info.WindowHandle;
        }

        /// <summary>
        /// Extracts PID hint from context by checking well-known launch step context keys.
        /// </summary>
        private int? ExtractPidHintFromContext(IAutoLoginContext context)
        {
            // Check well-known keys using AutoLoginContextKeys
            var wellKnownKeys = new[]
            {
                AutoLoginContextKeys.WellKnown.WindowerProcessId,
                AutoLoginContextKeys.WellKnown.POLProxyProcessId,
                AutoLoginContextKeys.WellKnown.PlayOnlineProcessId,
                AutoLoginContextKeys.WellKnown.FFXIProcessId,
                AutoLoginContextKeys.WellKnown.AshitaProcessId
            };

            foreach (var key in wellKnownKeys)
            {
                var pid = context.GetValueData<int>(key);
                if (pid > 0)
                {
                    return pid;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets window information for a specific process with smart window selection.
        /// </summary>
        private async Task<ProcessWindowInfo?> GetWindowInfoForProcessAsync(int processId, string applicationName)
        {
            var windows = await _processUtilityService.GetProcessWindowsAsync(processId);

            // Smart window selection: prefer main window over splash screens
            var mainWindow = windows.FirstOrDefault(w => w.IsVisible && w.IsMainWindow)
                          ?? windows.FirstOrDefault(w => w.IsVisible);

            var handle = mainWindow?.Handle ?? IntPtr.Zero;

            if (handle != IntPtr.Zero && _processUtilityService.IsWindowValid(handle))
            {
                await _loggingService.LogInfoAsync(
                    $"[WINDOW-DISCOVERY] Using window for {applicationName} (PID {processId}): " +
                    $"0x{handle.ToInt64():X} (Title: '{mainWindow?.Title ?? "Unknown"}')");

                return new ProcessWindowInfo
                {
                    ProcessId = processId,
                    WindowHandle = handle,
                    ApplicationName = applicationName,
                    WindowTitle = mainWindow?.Title ?? string.Empty
                };
            }

            return null;
        }

        /// <summary>
        /// Gets process ID from a window handle.
        /// </summary>
        private Task<int> GetProcessIdFromWindowHandleAsync(IntPtr windowHandle)
        {
            return Task.Run(() =>
            {
                try
                {
                    GetWindowThreadProcessId(windowHandle, out uint processId);
                    return (int)processId;
                }
                catch
                {
                    return 0;
                }
            });
        }

        /// <summary>
        /// Gets window title from a window handle.
        /// </summary>
        private Task<string> GetWindowTitleAsync(IntPtr windowHandle)
        {
            return Task.Run(() =>
            {
                try
                {
                    var length = GetWindowTextLength(windowHandle);
                    if (length == 0) return string.Empty;

                    var builder = new System.Text.StringBuilder(length + 1);
                    GetWindowText(windowHandle, builder, builder.Capacity);
                    return builder.ToString();
                }
                catch
                {
                    return string.Empty;
                }
            });
        }

        #region Win32 API Imports

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        #endregion
    }
}
