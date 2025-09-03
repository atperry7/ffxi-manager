using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Infrastructure;
using FFXIManager.Models.Settings;
using SharpDX.DirectInput;

namespace FFXIManager.Services
{
    /// <summary>
    /// DirectInput service for PlayStation and other non-Xbox controllers.
    /// Handles PS4 DualShock and PS5 DualSense controllers via DirectInput.
    /// </summary>
    public sealed class DirectInputControllerService : IDisposable
    {
        private const int POLLING_INTERVAL_MS = 16; // ~60fps polling
        private const int DEADZONE = 5000; // Analog stick deadzone

        // PS5 DualSense button mappings (based on DirectInput button indices)
        private const int PS5_BUTTON_SQUARE = 0;    // Square/X on Xbox layout
        private const int PS5_BUTTON_CROSS = 1;     // X/A on Xbox layout  
        private const int PS5_BUTTON_CIRCLE = 2;    // Circle/B on Xbox layout
        private const int PS5_BUTTON_TRIANGLE = 3;  // Triangle/Y on Xbox layout
        private const int PS5_BUTTON_L1 = 4;        // Left bumper
        private const int PS5_BUTTON_R1 = 5;        // Right bumper
        private const int PS5_BUTTON_L2 = 6;        // Left trigger
        private const int PS5_BUTTON_R2 = 7;        // Right trigger
        private const int PS5_BUTTON_CREATE = 8;    // Create/Select/Back
        private const int PS5_BUTTON_OPTIONS = 9;   // Options/Start
        private const int PS5_BUTTON_L3 = 10;       // Left stick click
        private const int PS5_BUTTON_R3 = 11;       // Right stick click
        private const int PS5_BUTTON_PS = 12;       // PlayStation button
        private const int PS5_BUTTON_TOUCHPAD = 13; // Touchpad click

        private readonly ILoggingService _loggingService;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly ConcurrentDictionary<ControllerButton, int> _registeredButtons = new();
        private readonly ConcurrentDictionary<Guid, ControllerButtonPressState> _lastButtonStates = new();
        
        private DirectInput? _directInput;
        private readonly List<Joystick> _joysticks = new();
        private Task? _pollingTask;
        private volatile bool _disposed;

        public event EventHandler<DirectInputButtonPressedEventArgs>? ButtonPressed;
        public bool IsAnyControllerConnected { get; private set; }

        public DirectInputControllerService()
        {
            _loggingService = ServiceLocator.LoggingService;
            Initialize();
        }

        private void Initialize()
        {
            try
            {
                _loggingService.LogInfoAsync("🎮 Initializing DirectInput for PlayStation controller support...", "DirectInputController");
                
                _directInput = new DirectInput();
                
                // Find all game controllers
                var devices = _directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);
                
                foreach (var deviceInstance in devices)
                {
                    try
                    {
                        // Check if it's a PlayStation controller
                        var name = deviceInstance.ProductName.ToLower();
                        var isPlayStation = name.Contains("playstation") || 
                                          name.Contains("dualsense") || 
                                          name.Contains("dualshock") ||
                                          name.Contains("ps5") || 
                                          name.Contains("ps4") ||
                                          name.Contains("wireless controller") || // Generic PS controller name
                                          deviceInstance.ProductGuid == new Guid("054c0ce5-0000-0000-0000-504944564944"); // PS5 controller GUID
                        
                        if (!isPlayStation)
                        {
                            _loggingService.LogInfoAsync($"⏭️ Skipping non-PlayStation controller: {deviceInstance.ProductName}", "DirectInputController");
                            continue;
                        }

                        var joystick = new Joystick(_directInput, deviceInstance.InstanceGuid);
                        
                        // Set buffer size for input
                        joystick.Properties.BufferSize = 128;
                        
                        // Acquire the joystick
                        joystick.Acquire();
                        
                        _joysticks.Add(joystick);
                        IsAnyControllerConnected = true;
                        
                        _loggingService.LogInfoAsync($"✅ PlayStation controller detected: {deviceInstance.ProductName} (GUID: {deviceInstance.InstanceGuid})", "DirectInputController");
                        _loggingService.LogInfoAsync($"   Type: {deviceInstance.Type}, Subtype: {deviceInstance.Subtype}", "DirectInputController");
                    }
                    catch (Exception ex)
                    {
                        _loggingService.LogErrorAsync($"Failed to initialize controller: {deviceInstance.ProductName}", ex, "DirectInputController");
                    }
                }

                if (_joysticks.Count == 0)
                {
                    _loggingService.LogInfoAsync("🔍 No PlayStation controllers detected via DirectInput", "DirectInputController");
                }
                else
                {
                    // Start polling
                    StartPolling();
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync("Failed to initialize DirectInput", ex, "DirectInputController");
            }
        }

        public bool RegisterButton(int hotkeyId, ControllerButton button)
        {
            if (_disposed) return false;
            if (button == ControllerButton.None) return false;

            try
            {
                _registeredButtons.TryAdd(button, hotkeyId);
                _loggingService.LogInfoAsync($"✓ Registered DirectInput button: {button.GetDescription()} (ID: {hotkeyId})", "DirectInputController");
                return true;
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync($"Failed to register DirectInput button: {button}", ex, "DirectInputController");
                return false;
            }
        }

        public void UnregisterAll()
        {
            if (_disposed) return;

            try
            {
                _registeredButtons.Clear();
                _lastButtonStates.Clear();
                _loggingService.LogInfoAsync("All DirectInput buttons unregistered", "DirectInputController");
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync("Error unregistering DirectInput buttons", ex, "DirectInputController");
            }
        }

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
                        break;
                    }
                    catch (Exception ex)
                    {
                        await _loggingService.LogErrorAsync("Error in DirectInput polling loop", ex, "DirectInputController");
                        await Task.Delay(1000, token);
                    }
                }
            });
        }

        private async Task PollControllersAsync(CancellationToken cancellationToken)
        {
            foreach (var joystick in _joysticks.ToList())
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    joystick.Poll();
                    var state = joystick.GetCurrentState();
                    
                    await ProcessControllerInputAsync(joystick.Information.InstanceGuid, state, cancellationToken);
                }
                catch (SharpDX.SharpDXException ex) when ((uint)ex.HResult == 0x8007001E) // DIERR_INPUTLOST
                {
                    // Controller disconnected
                    _loggingService.LogInfoAsync($"🔌 PlayStation controller disconnected: {joystick.Information.InstanceGuid}", "DirectInputController");
                    _joysticks.Remove(joystick);
                    joystick.Dispose();
                    IsAnyControllerConnected = _joysticks.Count > 0;
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync($"Error polling DirectInput controller", ex, "DirectInputController");
                }
            }
        }

        private async Task ProcessControllerInputAsync(Guid controllerId, JoystickState state, CancellationToken cancellationToken)
        {
            var currentButtons = GetPressedButtons(state);
            
            if (!_lastButtonStates.TryGetValue(controllerId, out var lastState))
            {
                lastState = new ControllerButtonPressState();
                _lastButtonStates[controllerId] = lastState;
            }

            // Check for newly pressed buttons
            foreach (var button in currentButtons)
            {
                if (!lastState.WasPressed(button))
                {
                    if (_registeredButtons.TryGetValue(button, out var hotkeyId))
                    {
                        var args = new DirectInputButtonPressedEventArgs(hotkeyId, button, controllerId);
                        ButtonPressed?.Invoke(this, args);

                        _ = _loggingService.LogInfoAsync(
                            $"🎮 DirectInput button pressed: {button.GetDescription()} (Controller {controllerId:N}, ID: {hotkeyId})",
                            "DirectInputController");
                    }
                }
            }

            lastState.UpdateState(currentButtons);
        }

        private static ControllerButton[] GetPressedButtons(JoystickState state)
        {
            var pressed = new List<ControllerButton>();

            // Map PlayStation buttons to our ControllerButton enum
            var buttons = state.Buttons;
            
            // Face buttons
            if (buttons[PS5_BUTTON_CROSS]) pressed.Add(ControllerButton.FaceButtonA);     // Cross -> A
            if (buttons[PS5_BUTTON_CIRCLE]) pressed.Add(ControllerButton.FaceButtonB);    // Circle -> B
            if (buttons[PS5_BUTTON_SQUARE]) pressed.Add(ControllerButton.FaceButtonX);    // Square -> X
            if (buttons[PS5_BUTTON_TRIANGLE]) pressed.Add(ControllerButton.FaceButtonY);  // Triangle -> Y
            
            // Shoulder buttons
            if (buttons[PS5_BUTTON_L1]) pressed.Add(ControllerButton.LeftBumper);
            if (buttons[PS5_BUTTON_R1]) pressed.Add(ControllerButton.RightBumper);
            
            // Triggers (on PS5, these are also digital buttons)
            if (buttons[PS5_BUTTON_L2]) pressed.Add(ControllerButton.LeftTrigger);
            if (buttons[PS5_BUTTON_R2]) pressed.Add(ControllerButton.RightTrigger);
            
            // System buttons
            if (buttons[PS5_BUTTON_OPTIONS]) pressed.Add(ControllerButton.Start);
            if (buttons[PS5_BUTTON_CREATE]) pressed.Add(ControllerButton.Select);
            
            // Thumbstick clicks
            if (buttons[PS5_BUTTON_L3]) pressed.Add(ControllerButton.LeftThumbstickClick);
            if (buttons[PS5_BUTTON_R3]) pressed.Add(ControllerButton.RightThumbstickClick);
            
            // D-Pad (using POV hat)
            var pov = state.PointOfViewControllers[0];
            if (pov != -1)
            {
                // POV values are in hundredths of degrees (0-35999)
                // 0 = up, 9000 = right, 18000 = down, 27000 = left
                if (pov >= 31500 || pov <= 4500) pressed.Add(ControllerButton.DPadUp);
                if (pov >= 4500 && pov <= 13500) pressed.Add(ControllerButton.DPadRight);
                if (pov >= 13500 && pov <= 22500) pressed.Add(ControllerButton.DPadDown);
                if (pov >= 22500 && pov <= 31500) pressed.Add(ControllerButton.DPadLeft);
            }

            return pressed.ToArray();
        }

        public void RefreshControllers()
        {
            try
            {
                // Dispose existing joysticks
                foreach (var joystick in _joysticks)
                {
                    try
                    {
                        joystick.Unacquire();
                        joystick.Dispose();
                    }
                    catch { }
                }
                _joysticks.Clear();

                // Re-initialize
                Initialize();
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync("Error refreshing DirectInput controllers", ex, "DirectInputController");
            }
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

                foreach (var joystick in _joysticks)
                {
                    try
                    {
                        joystick.Unacquire();
                        joystick.Dispose();
                    }
                    catch { }
                }
                _joysticks.Clear();

                _directInput?.Dispose();
                
                _loggingService.LogInfoAsync("DirectInputControllerService disposed", "DirectInputController");
            }
            catch (Exception ex)
            {
                _loggingService?.LogErrorAsync("Error during DirectInputControllerService disposal", ex, "DirectInputController");
            }

            GC.SuppressFinalize(this);
        }

        private sealed class ControllerButtonPressState
        {
            private readonly HashSet<ControllerButton> _pressedButtons = new();

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

    public class DirectInputButtonPressedEventArgs : EventArgs
    {
        public int HotkeyId { get; }
        public ControllerButton Button { get; }
        public Guid ControllerId { get; }

        public DirectInputButtonPressedEventArgs(int hotkeyId, ControllerButton button, Guid controllerId)
        {
            HotkeyId = hotkeyId;
            Button = button;
            ControllerId = controllerId;
        }
    }
}