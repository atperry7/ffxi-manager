using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models.Settings;
using FFXIManager.Infrastructure;

namespace FFXIManager.Services
{
    /// <summary>
    /// Controller input service using XInput API for Xbox controllers and compatible devices.
    /// Mirrors the architecture of LowLevelHotkeyService for consistency.
    /// </summary>
    public sealed class ControllerInputService : IDisposable
    {
        private const int CONTROLLER_COUNT = 4; // XInput supports up to 4 controllers
        private const int POLLING_INTERVAL_MS = 16; // ~60fps polling for responsive input

        // XInput button flags
        private const ushort XINPUT_GAMEPAD_DPAD_UP = 0x0001;
        private const ushort XINPUT_GAMEPAD_DPAD_DOWN = 0x0002;
        private const ushort XINPUT_GAMEPAD_DPAD_LEFT = 0x0004;
        private const ushort XINPUT_GAMEPAD_DPAD_RIGHT = 0x0008;
        private const ushort XINPUT_GAMEPAD_START = 0x0010;
        private const ushort XINPUT_GAMEPAD_BACK = 0x0020;
        private const ushort XINPUT_GAMEPAD_LEFT_THUMB = 0x0040;
        private const ushort XINPUT_GAMEPAD_RIGHT_THUMB = 0x0080;
        private const ushort XINPUT_GAMEPAD_LEFT_SHOULDER = 0x0100;
        private const ushort XINPUT_GAMEPAD_RIGHT_SHOULDER = 0x0200;
        private const ushort XINPUT_GAMEPAD_A = 0x1000;
        private const ushort XINPUT_GAMEPAD_B = 0x2000;
        private const ushort XINPUT_GAMEPAD_X = 0x4000;
        private const ushort XINPUT_GAMEPAD_Y = 0x8000;

        // Trigger threshold for digital trigger press detection
        private const byte TRIGGER_THRESHOLD = 200; // Out of 255

        private readonly ConcurrentDictionary<ControllerButton, int> _registeredButtons = new();
        private readonly ConcurrentDictionary<int, ControllerButtonPressState> _lastButtonStates = new();
        private readonly ILoggingService _loggingService;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private Task? _pollingTask;
        private volatile bool _disposed;

        /// <summary>
        /// Event fired when a registered controller button is pressed
        /// </summary>
        public event EventHandler<ControllerButtonPressedEventArgs>? ButtonPressed;

        /// <summary>
        /// Gets whether any controller is currently connected
        /// </summary>
        public bool IsAnyControllerConnected { get; private set; }

        public ControllerInputService()
        {
            _loggingService = ServiceLocator.LoggingService;
            
            // Immediately check for controller connection (synchronous check)
            CheckInitialControllerConnection();
            
            StartPolling();
            _loggingService.LogInfoAsync("🎮 ControllerInputService initialized with XInput polling", "ControllerInputService");
        }

        /// <summary>
        /// Registers a controller button for monitoring
        /// </summary>
        /// <param name="hotkeyId">Unique identifier for this button binding</param>
        /// <param name="button">The controller button to monitor</param>
        /// <returns>True if registration was successful</returns>
        public bool RegisterButton(int hotkeyId, ControllerButton button)
        {
            if (_disposed) return false;
            if (button == ControllerButton.None) return false;

            try
            {
                _registeredButtons.TryAdd(button, hotkeyId);
                _loggingService.LogInfoAsync($"✓ Registered controller button: {button.GetDescription()} (ID: {hotkeyId})", "ControllerInputService");
                return true;
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync($"Failed to register controller button: {button}", ex, "ControllerInputService");
                return false;
            }
        }

        /// <summary>
        /// Unregisters all controller buttons
        /// </summary>
        public void UnregisterAll()
        {
            if (_disposed) return;

            try
            {
                _registeredButtons.Clear();
                _lastButtonStates.Clear();
                _loggingService.LogInfoAsync("All controller buttons unregistered", "ControllerInputService");
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync("Error unregistering controller buttons", ex, "ControllerInputService");
            }
        }

        /// <summary>
        /// Starts the controller polling loop
        /// </summary>
        private void StartPolling()
        {
            _pollingTask = Task.Run(async () =>
            {
                var token = _cancellationTokenSource.Token;

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await PollControllersAsync(token);
                        await Task.Delay(POLLING_INTERVAL_MS, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break; // Normal cancellation
                    }
                    catch (Exception ex)
                    {
                        await _loggingService.LogErrorAsync("Error in controller polling loop", ex, "ControllerInputService");
                        // Continue polling after error
                        await Task.Delay(1000, token); // Brief pause before retry
                    }
                }
            });
        }

        /// <summary>
        /// Polls all controllers for input changes
        /// </summary>
        private async Task PollControllersAsync(CancellationToken cancellationToken)
        {
            bool anyConnected = false;

            for (int controllerId = 0; controllerId < CONTROLLER_COUNT; controllerId++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var state = new XINPUT_STATE();
                var result = XInputGetState(controllerId, ref state);

                if (result == 0) // ERROR_SUCCESS
                {
                    anyConnected = true;
                    await ProcessControllerInputAsync(controllerId, state.Gamepad, cancellationToken);
                }
            }

            // Update connection status
            if (IsAnyControllerConnected != anyConnected)
            {
                IsAnyControllerConnected = anyConnected;
                var status = anyConnected ? "connected" : "disconnected";
                await _loggingService.LogInfoAsync($"🎮 Controller status changed: {status}", "ControllerInputService");
            }
        }

        /// <summary>
        /// Processes input changes for a specific controller
        /// </summary>
        private async Task ProcessControllerInputAsync(int controllerId, XINPUT_GAMEPAD gamepad, CancellationToken cancellationToken)
        {
            var currentButtons = GetPressedButtons(gamepad);
            
            if (!_lastButtonStates.TryGetValue(controllerId, out var lastState))
            {
                lastState = new ControllerButtonPressState();
                _lastButtonStates[controllerId] = lastState;
            }

            // Debug: Log pressed buttons if any are detected
            if (currentButtons.Length > 0)
            {
                var buttonNames = string.Join(", ", currentButtons.Select(b => b.GetDescription()));
                System.Diagnostics.Debug.WriteLine($"🎮 Controller {controllerId} buttons pressed: {buttonNames}");
            }

            // Check for newly pressed buttons (edge detection)
            foreach (var button in currentButtons)
            {
                if (!lastState.WasPressed(button))
                {
                    System.Diagnostics.Debug.WriteLine($"🎮 New button press detected: {button.GetDescription()}");
                    
                    if (_registeredButtons.TryGetValue(button, out var hotkeyId))
                    {
                        // Button was just pressed - fire event
                        var args = new ControllerButtonPressedEventArgs(hotkeyId, button, controllerId);
                        ButtonPressed?.Invoke(this, args);

                        await _loggingService.LogInfoAsync(
                            $"🎮 Controller button pressed: {button.GetDescription()} (Controller {controllerId}, ID: {hotkeyId})",
                            "ControllerInputService");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"🎮 Button {button.GetDescription()} not registered - no event fired");
                    }
                }
            }

            // Update state for next iteration
            lastState.UpdateState(currentButtons);
        }

        /// <summary>
        /// Converts XInput gamepad state to our ControllerButton enumeration
        /// </summary>
        private static ControllerButton[] GetPressedButtons(XINPUT_GAMEPAD gamepad)
        {
            var pressed = new System.Collections.Generic.List<ControllerButton>();

            // D-Pad
            if ((gamepad.wButtons & XINPUT_GAMEPAD_DPAD_UP) != 0) pressed.Add(ControllerButton.DPadUp);
            if ((gamepad.wButtons & XINPUT_GAMEPAD_DPAD_DOWN) != 0) pressed.Add(ControllerButton.DPadDown);
            if ((gamepad.wButtons & XINPUT_GAMEPAD_DPAD_LEFT) != 0) pressed.Add(ControllerButton.DPadLeft);
            if ((gamepad.wButtons & XINPUT_GAMEPAD_DPAD_RIGHT) != 0) pressed.Add(ControllerButton.DPadRight);

            // Face buttons
            if ((gamepad.wButtons & XINPUT_GAMEPAD_A) != 0) pressed.Add(ControllerButton.FaceButtonA);
            if ((gamepad.wButtons & XINPUT_GAMEPAD_B) != 0) pressed.Add(ControllerButton.FaceButtonB);
            if ((gamepad.wButtons & XINPUT_GAMEPAD_X) != 0) pressed.Add(ControllerButton.FaceButtonX);
            if ((gamepad.wButtons & XINPUT_GAMEPAD_Y) != 0) pressed.Add(ControllerButton.FaceButtonY);

            // Shoulder buttons
            if ((gamepad.wButtons & XINPUT_GAMEPAD_LEFT_SHOULDER) != 0) pressed.Add(ControllerButton.LeftBumper);
            if ((gamepad.wButtons & XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0) pressed.Add(ControllerButton.RightBumper);

            // Triggers (analog to digital conversion)
            if (gamepad.bLeftTrigger > TRIGGER_THRESHOLD) pressed.Add(ControllerButton.LeftTrigger);
            if (gamepad.bRightTrigger > TRIGGER_THRESHOLD) pressed.Add(ControllerButton.RightTrigger);

            // System buttons
            if ((gamepad.wButtons & XINPUT_GAMEPAD_START) != 0) pressed.Add(ControllerButton.Start);
            if ((gamepad.wButtons & XINPUT_GAMEPAD_BACK) != 0) pressed.Add(ControllerButton.Select);

            // Thumbstick clicks
            if ((gamepad.wButtons & XINPUT_GAMEPAD_LEFT_THUMB) != 0) pressed.Add(ControllerButton.LeftThumbstickClick);
            if ((gamepad.wButtons & XINPUT_GAMEPAD_RIGHT_THUMB) != 0) pressed.Add(ControllerButton.RightThumbstickClick);

            return pressed.ToArray();
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            try
            {
                _cancellationTokenSource.Cancel();
                _pollingTask?.Wait(TimeSpan.FromSeconds(2));
                _cancellationTokenSource.Dispose();
                _loggingService.LogInfoAsync("ControllerInputService disposed", "ControllerInputService");
            }
            catch (Exception ex)
            {
                _loggingService?.LogErrorAsync("Error during ControllerInputService disposal", ex, "ControllerInputService");
            }

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Performs an immediate synchronous check for connected controllers
        /// </summary>
        private void CheckInitialControllerConnection()
        {
            try
            {
                _loggingService.LogInfoAsync("🔍 Starting initial controller detection...", "ControllerInputService");
                _loggingService.LogInfoAsync($"🔍 Windows Version: {Environment.OSVersion}", "ControllerInputService");
                _loggingService.LogInfoAsync($"🔍 XInput version being used: {GetXInputVersionInfo()}", "ControllerInputService");
                
                // Windows 11 specific check
                if (IsWindows11())
                {
                    _loggingService.LogInfoAsync("🪟 Windows 11 detected - checking Gaming Services and Xbox Game Bar", "ControllerInputService");
                    CheckWindows11GamingServices();
                }
                
                for (int controllerId = 0; controllerId < CONTROLLER_COUNT; controllerId++)
                {
                    var state = new XINPUT_STATE();
                    var result = XInputGetState(controllerId, ref state);
                    
                    _loggingService.LogInfoAsync($"🎮 XInput slot {controllerId}: Result={result} (0=connected, 1167=disconnected)", "ControllerInputService");

                    if (result == 0) // ERROR_SUCCESS
                    {
                        IsAnyControllerConnected = true;
                        _loggingService.LogInfoAsync($"🎮 Controller detected on startup: Controller {controllerId} (PacketNumber: {state.dwPacketNumber})", "ControllerInputService");
                        return; // Found at least one, no need to check others
                    }
                    else
                    {
                        // Log additional error codes for Xbox Elite troubleshooting
                        var errorMessage = result switch
                        {
                            1167 => "ERROR_DEVICE_NOT_CONNECTED - No controller in this slot",
                            1168 => "ERROR_INVALID_DEVICE_OBJECT_PARAMETER - Invalid controller state",
                            87 => "ERROR_INVALID_PARAMETER - Invalid slot number",
                            _ => $"Unknown XInput error code: {result}"
                        };
                        _loggingService.LogInfoAsync($"🔍 XInput slot {controllerId} details: {errorMessage}", "ControllerInputService");
                    }
                }
                
                IsAnyControllerConnected = false;
                _loggingService.LogInfoAsync("🎮 No XInput controllers detected during initial check", "ControllerInputService");
                _loggingService.LogInfoAsync("🔍 Xbox Elite Series 2 troubleshooting: Check Xbox Accessories app, try different USB port, or restart Xbox drivers", "ControllerInputService");
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync("Error during initial controller detection", ex, "ControllerInputService");
                IsAnyControllerConnected = false;
            }
        }

        /// <summary>
        /// Gets information about which XInput version is being used
        /// </summary>
        private string GetXInputVersionInfo()
        {
            return _workingXInputVersion switch
            {
                1 => "XInput 1.4 (Windows 8+)",
                2 => "XInput 1.3 (Windows Vista/7)", 
                3 => "XInput 9.1.0 (Windows XP)",
                -1 => "No XInput runtime available",
                _ => "XInput version not yet determined"
            };
        }

        /// <summary>
        /// Checks if running on Windows 11
        /// </summary>
        private static bool IsWindows11()
        {
            var version = Environment.OSVersion.Version;
            return version.Major >= 10 && version.Build >= 22000; // Windows 11 build number
        }

        /// <summary>
        /// Performs Windows 11 specific gaming services checks
        /// </summary>
        private void CheckWindows11GamingServices()
        {
            try
            {
                // Check Xbox Game Bar process
                var gameBarProcesses = System.Diagnostics.Process.GetProcessesByName("GameBar");
                _loggingService.LogInfoAsync($"🎮 Xbox Game Bar processes: {gameBarProcesses.Length}", "ControllerInputService");

                // Check for Xbox-related services
                var xboxServices = new[] { "XboxGipSvc", "XboxNetApiSvc", "XblAuthManager", "XblGameSave" };
                foreach (var serviceName in xboxServices)
                {
                    try
                    {
                        using var service = new System.ServiceProcess.ServiceController(serviceName);
                        _loggingService.LogInfoAsync($"🔧 Service {serviceName}: {service.Status}", "ControllerInputService");
                    }
                    catch
                    {
                        _loggingService.LogInfoAsync($"🔧 Service {serviceName}: Not found or inaccessible", "ControllerInputService");
                    }
                }

                // Check Gaming Services registry
                try
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\GamingServices");
                    if (key != null)
                    {
                        _loggingService.LogInfoAsync("🎮 Windows Gaming Services registry entry found", "ControllerInputService");
                    }
                    else
                    {
                        _loggingService.LogWarningAsync("⚠️ Windows Gaming Services registry entry missing - may need reinstall", "ControllerInputService");
                    }
                }
                catch (Exception ex)
                {
                    _loggingService.LogWarningAsync($"⚠️ Could not check Gaming Services registry: {ex.Message}", "ControllerInputService");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync("Error checking Windows 11 gaming services", ex, "ControllerInputService");
            }
        }

        // XInput P/Invoke declarations with fallback versions
        [DllImport("xinput1_4.dll", SetLastError = true, EntryPoint = "XInputGetState")]
        private static extern int XInputGetState_1_4(int dwUserIndex, ref XINPUT_STATE pState);

        [DllImport("xinput1_3.dll", SetLastError = true, EntryPoint = "XInputGetState")]
        private static extern int XInputGetState_1_3(int dwUserIndex, ref XINPUT_STATE pState);

        [DllImport("xinput9_1_0.dll", SetLastError = true, EntryPoint = "XInputGetState")]
        private static extern int XInputGetState_9_1_0(int dwUserIndex, ref XINPUT_STATE pState);

        // Track which XInput version is working
        private static int _workingXInputVersion = 0; // 0 = not tested, 1 = v1.4, 2 = v1.3, 3 = v9.1.0, -1 = none work

        /// <summary>
        /// Tries different XInput versions for broader compatibility
        /// </summary>
        private static int XInputGetState(int dwUserIndex, ref XINPUT_STATE pState)
        {
            // If we already found a working version, use it directly
            if (_workingXInputVersion > 0)
            {
                return _workingXInputVersion switch
                {
                    1 => XInputGetState_1_4(dwUserIndex, ref pState),
                    2 => XInputGetState_1_3(dwUserIndex, ref pState),
                    3 => XInputGetState_9_1_0(dwUserIndex, ref pState),
                    _ => 1167 // ERROR_DEVICE_NOT_CONNECTED
                };
            }

            // Try to find a working version
            try
            {
                // Try XInput 1.4 first (Windows 8+)
                var result = XInputGetState_1_4(dwUserIndex, ref pState);
                _workingXInputVersion = 1;
                ServiceLocator.LoggingService.LogInfoAsync("✅ Using XInput 1.4 (xinput1_4.dll)", "ControllerInputService");
                return result;
            }
            catch (DllNotFoundException)
            {
                try
                {
                    // Fall back to XInput 1.3 (Windows Vista/7)
                    var result = XInputGetState_1_3(dwUserIndex, ref pState);
                    _workingXInputVersion = 2;
                    ServiceLocator.LoggingService.LogInfoAsync("✅ Using XInput 1.3 (xinput1_3.dll)", "ControllerInputService");
                    return result;
                }
                catch (DllNotFoundException)
                {
                    try
                    {
                        // Fall back to XInput 9.1.0 (Windows XP)
                        var result = XInputGetState_9_1_0(dwUserIndex, ref pState);
                        _workingXInputVersion = 3;
                        ServiceLocator.LoggingService.LogInfoAsync("✅ Using XInput 9.1.0 (xinput9_1_0.dll)", "ControllerInputService");
                        return result;
                    }
                    catch (DllNotFoundException)
                    {
                        // No XInput available
                        _workingXInputVersion = -1;
                        ServiceLocator.LoggingService.LogWarningAsync("❌ No XInput runtime found - controller support disabled", "ControllerInputService");
                        return 1167; // ERROR_DEVICE_NOT_CONNECTED
                    }
                }
            }
            catch (EntryPointNotFoundException)
            {
                // Function exists but wrong entry point - shouldn't happen now
                _workingXInputVersion = -1;
                return 1167;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        /// <summary>
        /// Tracks button press state for edge detection
        /// </summary>
        private sealed class ControllerButtonPressState
        {
            private readonly System.Collections.Generic.HashSet<ControllerButton> _pressedButtons = new();

            public bool WasPressed(ControllerButton button) => _pressedButtons.Contains(button);

            public void UpdateState(ControllerButton[] currentButtons)
            {
                _pressedButtons.Clear();
                foreach (var button in currentButtons)
                {
                    _pressedButtons.Add(button);
                }
            }
        }
    }

    /// <summary>
    /// Event arguments for controller button press events
    /// </summary>
    public class ControllerButtonPressedEventArgs : EventArgs
    {
        public int HotkeyId { get; }
        public ControllerButton Button { get; }
        public int ControllerId { get; }

        public ControllerButtonPressedEventArgs(int hotkeyId, ControllerButton button, int controllerId)
        {
            HotkeyId = hotkeyId;
            Button = button;
            ControllerId = controllerId;
        }
    }
}