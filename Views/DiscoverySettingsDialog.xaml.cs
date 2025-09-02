using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using FFXIManager.Services;

namespace FFXIManager.Views
{
    public partial class DiscoverySettingsDialog : Window
    {
        private DispatcherTimer? _controllerStatusTimer;

        public DiscoverySettingsDialog()
        {
            InitializeComponent();
            
            // Check controller status after UI loads and start periodic updates
            Loaded += async (s, e) => 
            {
                await UpdateControllerStatusAsync();
                StartControllerStatusTimer();
            };

            // Stop timer when window closes
            Closing += (s, e) => StopControllerStatusTimer();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.DiscoverySettingsViewModel vm)
            {
                vm.Save();
            }
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Updates the controller status indicator based on current controller connection state.
        /// </summary>
        private async Task UpdateControllerStatusAsync()
        {
            try
            {
                // Use the existing GlobalHotkeyManager's controller service if available
                var globalManager = GlobalHotkeyManager.Instance;
                if (globalManager != null)
                {
                    // Check if we can access the controller service status
                    var isConnected = CheckControllerConnectionDirect();
                    
                    ControllerStatusText.Text = isConnected ? 
                        "Controller: Connected ✅" : 
                        "Controller: Not detected";
                }
                else
                {
                    // Fallback: create temporary service with longer detection time
                    using var controllerService = new ControllerInputService();
                    
                    // Allow more time for XInput detection (controllers can take a moment to enumerate)
                    await Task.Delay(500);
                    
                    var isConnected = controllerService.IsAnyControllerConnected;
                    
                    ControllerStatusText.Text = isConnected ? 
                        "Controller: Connected ✅" : 
                        "Controller: Not detected";
                }
            }
            catch (Exception ex)
            {
                ControllerStatusText.Text = "Controller: Status unknown";
                System.Diagnostics.Debug.WriteLine($"Controller status check failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Directly checks for controller connection using XInput API
        /// </summary>
        private static bool CheckControllerConnectionDirect()
        {
            try
            {
                // First, log what we find
                System.Diagnostics.Debug.WriteLine("=== Controller Detection Debug ===");
                
                // Check all 4 possible XInput controller slots
                for (int i = 0; i < 4; i++)
                {
                    var state = new XINPUT_STATE();
                    var result = XInputGetState(i, ref state);
                    System.Diagnostics.Debug.WriteLine($"XInput Slot {i}: Result={result} (0=connected, 1167=not_connected)");
                    
                    if (result == 0) // ERROR_SUCCESS means controller is connected
                    {
                        System.Diagnostics.Debug.WriteLine($"✅ Found controller on slot {i}!");
                        return true;
                    }
                }
                
                // Also check Windows registry for game controllers
                try
                {
                    System.Diagnostics.Debug.WriteLine("Checking Windows Game Controllers registry...");
                    var hasControllers = CheckWindowsGameControllers();
                    System.Diagnostics.Debug.WriteLine($"Windows Game Controllers detected: {hasControllers}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Registry check failed: {ex.Message}");
                }
                
                System.Diagnostics.Debug.WriteLine("=== End Controller Debug ===");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Controller detection error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks for controllers using Windows Registry (Game Controllers)
        /// </summary>
        private static bool CheckWindowsGameControllers()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"System\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM");
                if (key != null)
                {
                    var subKeyNames = key.GetSubKeyNames();
                    System.Diagnostics.Debug.WriteLine($"Found {subKeyNames.Length} registered game controllers in registry");
                    return subKeyNames.Length > 0;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        // XInput P/Invoke for direct controller checking with fallbacks
        [System.Runtime.InteropServices.DllImport("xinput1_4.dll", SetLastError = true, EntryPoint = "XInputGetState")]
        private static extern int XInputGetState_1_4(int dwUserIndex, ref XINPUT_STATE pState);

        [System.Runtime.InteropServices.DllImport("xinput1_3.dll", SetLastError = true, EntryPoint = "XInputGetState")]
        private static extern int XInputGetState_1_3(int dwUserIndex, ref XINPUT_STATE pState);

        [System.Runtime.InteropServices.DllImport("xinput9_1_0.dll", SetLastError = true, EntryPoint = "XInputGetState")]
        private static extern int XInputGetState_9_1_0(int dwUserIndex, ref XINPUT_STATE pState);

        /// <summary>
        /// Tries different XInput versions for broader compatibility
        /// </summary>
        private static int XInputGetState(int dwUserIndex, ref XINPUT_STATE pState)
        {
            try
            {
                // Try XInput 1.4 first (Windows 8+)
                return XInputGetState_1_4(dwUserIndex, ref pState);
            }
            catch (System.DllNotFoundException)
            {
                try
                {
                    // Fall back to XInput 1.3 (Windows Vista/7)
                    return XInputGetState_1_3(dwUserIndex, ref pState);
                }
                catch (System.DllNotFoundException)
                {
                    try
                    {
                        // Fall back to XInput 9.1.0 (Windows XP)
                        return XInputGetState_9_1_0(dwUserIndex, ref pState);
                    }
                    catch (System.DllNotFoundException)
                    {
                        // No XInput available
                        return 1167; // ERROR_DEVICE_NOT_CONNECTED
                    }
                }
            }
            catch (System.EntryPointNotFoundException)
            {
                // Function exists but wrong entry point - shouldn't happen now
                return 1167;
            }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
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
        /// Starts periodic controller status checking for hotplug detection
        /// </summary>
        private void StartControllerStatusTimer()
        {
            _controllerStatusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2) // Check every 2 seconds for changes
            };
            _controllerStatusTimer.Tick += async (s, e) => await UpdateControllerStatusAsync();
            _controllerStatusTimer.Start();
        }

        /// <summary>
        /// Stops the controller status timer
        /// </summary>
        private void StopControllerStatusTimer()
        {
            _controllerStatusTimer?.Stop();
            _controllerStatusTimer = null;
        }

        /// <summary>
        /// Handles the refresh controller button click
        /// </summary>
        private async void RefreshController_Click(object sender, RoutedEventArgs e)
        {
            ControllerStatusText.Text = "Controller: Checking...";
            await Task.Delay(50); // Brief UI update delay
            await UpdateControllerStatusAsync();
        }
    }
}
