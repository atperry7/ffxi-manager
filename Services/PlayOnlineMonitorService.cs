using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services
{
    /// <summary>
    /// Interface for monitoring PlayOnline character instances
    /// </summary>
    public interface IPlayOnlineMonitorService
    {
        Task<List<PlayOnlineCharacter>> GetRunningCharactersAsync();
        Task<bool> ActivateCharacterWindowAsync(PlayOnlineCharacter character);
        Task RefreshCharacterDataAsync();
        void StartMonitoring();
        void StopMonitoring();
        event EventHandler<PlayOnlineCharacter>? CharacterDetected;
        event EventHandler<PlayOnlineCharacter>? CharacterUpdated;
        event EventHandler<int>? CharacterRemoved; // ProcessId
    }

    /// <summary>
    /// Enhanced service for monitoring and managing PlayOnline character instances
    /// Addresses IOException issues and implements improved error handling
    /// </summary>
    public class PlayOnlineMonitorService : IPlayOnlineMonitorService, IDisposable
    {
        private readonly ILoggingService _loggingService;
        private readonly List<PlayOnlineCharacter> _characters = new();
        private readonly System.Threading.Timer? _monitoringTimer;
        private readonly SemaphoreSlim _activationSemaphore = new(1, 1);
        private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
        private bool _isMonitoring;
        private bool _disposed;

        // Reduced polling frequency to minimize I/O exceptions
        private const int MONITOR_INTERVAL_MS = 3000; // Increased to 3 seconds to reduce I/O pressure
        private const int ACTIVATION_COOLDOWN_MS = 200; // Increased cooldown to prevent rapid switching issues
        private const int PROCESS_TIMEOUT_MS = 5000; // Timeout for process operations

        // Windows API imports for window management
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SwitchToThisWindow(IntPtr hWnd, bool fAltTab);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;
        private const int SW_MAXIMIZE = 3;

        public event EventHandler<PlayOnlineCharacter>? CharacterDetected;
        public event EventHandler<PlayOnlineCharacter>? CharacterUpdated;
        public event EventHandler<int>? CharacterRemoved;

        public PlayOnlineMonitorService(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            
            // Monitor character instances with reduced frequency
            _monitoringTimer = new System.Threading.Timer(MonitorCharacters, null,
                TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(MONITOR_INTERVAL_MS));
        }

        public async Task<List<PlayOnlineCharacter>> GetRunningCharactersAsync()
        {
            await RefreshCharacterDataAsync();
            return new List<PlayOnlineCharacter>(_characters);
        }

        public async Task<bool> ActivateCharacterWindowAsync(PlayOnlineCharacter character)
        {
            if (character == null || character.WindowHandle == IntPtr.Zero)
            {
                await _loggingService.LogWarningAsync($"Cannot activate character: null character or invalid window handle", "PlayOnlineMonitorService");
                return false;
            }

            // Use semaphore to prevent rapid successive activation attempts
            if (!await _activationSemaphore.WaitAsync(ACTIVATION_COOLDOWN_MS))
            {
                await _loggingService.LogDebugAsync("Activation already in progress, skipping...", "PlayOnlineMonitorService");
                return false;
            }

            try
            {
                return await ActivateCharacterWindowInternal(character);
            }
            finally
            {
                _activationSemaphore.Release();
            }
        }

        private async Task<bool> ActivateCharacterWindowInternal(PlayOnlineCharacter character)
        {
            try
            {
                await _loggingService.LogInfoAsync($"Attempting to activate window for character: {character.DisplayName} (Handle: {character.WindowHandle:X8}, PID: {character.ProcessId})", "PlayOnlineMonitorService");

                // Check if window still exists
                if (!IsWindow(character.WindowHandle))
                {
                    await _loggingService.LogWarningAsync($"Window handle {character.WindowHandle:X8} is no longer valid for {character.DisplayName}", "PlayOnlineMonitorService");
                    return false;
                }

                bool success = false;

                // Method 1: Standard activation with timeout protection
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(PROCESS_TIMEOUT_MS));
                try
                {
                    await Task.Run(() =>
                    {
                        // Restore window if minimized
                        if (IsIconic(character.WindowHandle))
                        {
                            ShowWindow(character.WindowHandle, SW_RESTORE);
                            Thread.Sleep(100); // Small delay for restore
                        }
                        else
                        {
                            ShowWindow(character.WindowHandle, SW_SHOW);
                        }

                        // Try to bring to foreground
                        success = SetForegroundWindow(character.WindowHandle);
                    }, cts.Token);

                    await _loggingService.LogInfoAsync($"Standard activation result: {success}", "PlayOnlineMonitorService");
                }
                catch (OperationCanceledException)
                {
                    await _loggingService.LogWarningAsync($"Activation timeout for {character.DisplayName}", "PlayOnlineMonitorService");
                }
                catch (Exception ex)
                {
                    await _loggingService.LogWarningAsync($"Standard activation failed: {ex.Message}", "PlayOnlineMonitorService");
                }

                // Method 2: Alternative approach if standard failed
                if (!success && !cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Run(() =>
                        {
                            BringWindowToTop(character.WindowHandle);
                            SwitchToThisWindow(character.WindowHandle, true);
                            success = true; // Assume success for these methods
                        }, cts.Token);
                        
                        await _loggingService.LogInfoAsync($"Alternative activation completed", "PlayOnlineMonitorService");
                    }
                    catch (OperationCanceledException)
                    {
                        await _loggingService.LogWarningAsync($"Alternative activation timeout for {character.DisplayName}", "PlayOnlineMonitorService");
                    }
                    catch (Exception ex)
                    {
                        await _loggingService.LogWarningAsync($"Alternative activation failed: {ex.Message}", "PlayOnlineMonitorService");
                    }
                }

                if (success && !cts.Token.IsCancellationRequested)
                {
                    // Update active status for all characters
                    foreach (var ch in _characters)
                    {
                        ch.IsActive = ch.ProcessId == character.ProcessId;
                    }
                    
                    await _loggingService.LogInfoAsync($"Successfully activated window for {character.DisplayName}", "PlayOnlineMonitorService");

                    // Add delay to prevent rapid switching issues
                    await Task.Delay(ACTIVATION_COOLDOWN_MS);
                }
                else
                {
                    await _loggingService.LogWarningAsync($"Activation failed or timed out for {character.DisplayName}", "PlayOnlineMonitorService");
                }

                return success && !cts.Token.IsCancellationRequested;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error activating window for {character.DisplayName}", ex, "PlayOnlineMonitorService");
                return false;
            }
        }

        public async Task RefreshCharacterDataAsync()
        {
            if (_disposed) return;

            // Use semaphore to prevent concurrent refresh operations
            if (!await _refreshSemaphore.WaitAsync(100))
            {
                Debug.WriteLine("Refresh already in progress, skipping...");
                return;
            }

            try
            {
                var currentCharacters = new List<PlayOnlineCharacter>();
                var activeWindow = GetForegroundWindow();

                // Get all FFXI/PlayOnline processes with better error handling
                var processes = GetPlayOnlineProcesses();

                // Find windows for each process
                foreach (var process in processes)
                {
                    try
                    {
                        if (process.HasExited) continue;

                        var windows = await GetProcessWindowsAsync(process);
                        
                        foreach (var window in windows)
                        {
                            var character = await CreateCharacterFromWindowAsync(window.Handle, window.Title, process);
                            if (character != null)
                            {
                                character.IsActive = window.Handle == activeWindow;
                                currentCharacters.Add(character);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log but don't let individual process errors stop the entire refresh
                        Debug.WriteLine($"Error processing {process.ProcessName}: {ex.Message}");
                    }
                    finally
                    {
                        // Ensure process is disposed
                        try { process.Dispose(); } catch { }
                    }
                }

                // Update character list
                await UpdateCharacterListAsync(currentCharacters);
            }
            catch (Exception ex)
            {
                // Only log critical errors to prevent log flooding
                await _loggingService.LogErrorAsync("Critical error in RefreshCharacterDataAsync", ex, "PlayOnlineMonitorService");
            }
            finally
            {
                _refreshSemaphore.Release();
            }
        }

        private List<Process> GetPlayOnlineProcesses()
        {
            var processes = new List<Process>();
            var processNames = new[] { "pol", "ffxi", "PlayOnlineViewer" };
            
            foreach (var processName in processNames)
            {
                try
                {
                    var procs = Process.GetProcessesByName(processName);
                    processes.AddRange(procs.Where(IsValidProcess));
                }
                catch (Exception ex)
                {
                    // Don't flood logs with process enumeration errors
                    Debug.WriteLine($"Could not get processes for {processName}: {ex.Message}");
                }
            }

            return processes;
        }

        private bool IsValidProcess(Process process)
        {
            try
            {
                // More defensive validation to prevent IOException
                return process != null && 
                       !process.HasExited && 
                       process.Id > 0 && 
                       process.MainWindowHandle != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }

        public void StartMonitoring()
        {
            _isMonitoring = true;
            _loggingService.LogInfoAsync("Started PlayOnline character monitoring", "PlayOnlineMonitorService");
        }

        public void StopMonitoring()
        {
            _isMonitoring = false;
            _loggingService.LogInfoAsync("Stopped PlayOnline character monitoring", "PlayOnlineMonitorService");
        }

        private async void MonitorCharacters(object? state)
        {
            if (!_isMonitoring || _disposed) return;

            try
            {
                await RefreshCharacterDataAsync();
            }
            catch (Exception ex)
            {
                // Only log critical monitoring errors to prevent log flooding
                Debug.WriteLine($"Critical error during character monitoring: {ex.Message}");
            }
        }

        private async Task<List<(IntPtr Handle, string Title)>> GetProcessWindowsAsync(Process process)
        {
            var windows = new List<(IntPtr, string)>();
            
            try
            {
                // Use timeout for window enumeration
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(2000));
                
                await Task.Run(() =>
                {
                    // Enumerate all windows to find ones belonging to this process
                    EnumWindows((hWnd, lParam) =>
                    {
                        try
                        {
                            if (cts.Token.IsCancellationRequested) return false;
                            
                            if (IsWindowVisible(hWnd))
                            {
                                GetWindowThreadProcessId(hWnd, out uint windowProcessId);
                                
                                if (windowProcessId == process.Id)
                                {
                                    var title = GetWindowTitle(hWnd);
                                    
                                    // Filter out empty titles and certain system windows
                                    if (!string.IsNullOrEmpty(title) && 
                                        !title.StartsWith("Default IME") && 
                                        !title.StartsWith("MSCTFIME UI") &&
                                        !title.Equals("Program Manager"))
                                    {
                                        windows.Add((hWnd, title));
                                        Debug.WriteLine($"Found window for PID {process.Id}: '{title}' (Handle: {hWnd:X8})");
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Ignore individual window enumeration errors
                        }
                        return !cts.Token.IsCancellationRequested; // Continue enumeration unless cancelled
                    }, IntPtr.Zero);
                }, cts.Token);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"Window enumeration timeout for process {process.Id}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error enumerating windows for process {process.Id}: {ex.Message}");
            }

            return windows;
        }

        private string GetWindowTitle(IntPtr hWnd)
        {
            try
            {
                const int maxLength = 256;
                var title = new StringBuilder(maxLength);
                GetWindowText(hWnd, title, maxLength);
                return title.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private async Task<PlayOnlineCharacter?> CreateCharacterFromWindowAsync(IntPtr windowHandle, string windowTitle, Process process)
        {
            try
            {
                // Parse character name from window title
                var (characterName, serverName) = ParseCharacterFromTitle(windowTitle);
                
                var character = new PlayOnlineCharacter
                {
                    ProcessId = process.Id,
                    WindowHandle = windowHandle,
                    WindowTitle = windowTitle,
                    CharacterName = characterName,
                    ServerName = serverName,
                    ProcessName = process.ProcessName,
                    LastSeen = DateTime.UtcNow
                };

                return character;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating character from window: {ex.Message}");
                return null;
            }
        }

        private (string CharacterName, string ServerName) ParseCharacterFromTitle(string windowTitle)
        {
            // Common FFXI window title patterns:
            // "FINAL FANTASY XI - [Character Name] - [Server Name]"
            // "FINAL FANTASY XI - Character Name"
            // "PlayOnline Viewer - Character Name"
            // "Character Name - PlayOnline" (some versions)
            // Or just character name in some cases
            
            var characterName = string.Empty;
            var serverName = string.Empty;

            if (string.IsNullOrEmpty(windowTitle))
                return (characterName, serverName);

            try
            {
                // Remove common prefixes first
                var title = windowTitle
                    .Replace("FINAL FANTASY XI - ", "")
                    .Replace("PlayOnline Viewer - ", "")
                    .Replace("FFXI - ", "")
                    .Trim();

                // Look for pattern: [Character] - [Server]
                if (title.Contains(" - "))
                {
                    var parts = title.Split(" - ", StringSplitOptions.RemoveEmptyEntries);
                    
                    if (parts.Length >= 2)
                    {
                        characterName = parts[0].Trim();
                        serverName = parts[1].Trim();
                        
                        // Handle cases where server name might be "PlayOnline" or similar
                        if (serverName.Equals("PlayOnline", StringComparison.OrdinalIgnoreCase) ||
                            serverName.Equals("Viewer", StringComparison.OrdinalIgnoreCase))
                        {
                            serverName = string.Empty;
                        }
                    }
                    else if (parts.Length == 1)
                    {
                        characterName = parts[0].Trim();
                    }
                }
                else
                {
                    // No " - " separator, assume entire title is character name
                    characterName = title;
                }

                // Clean up bracketed names [CharacterName] -> CharacterName
                characterName = characterName.Trim('[', ']', ' ');
                serverName = serverName.Trim('[', ']', ' ');

                // If character name is still empty, use the window title
                if (string.IsNullOrEmpty(characterName))
                {
                    characterName = windowTitle;
                }
            }
            catch (Exception ex)
            {
                // If parsing fails, just use the window title as character name
                characterName = windowTitle;
                Debug.WriteLine($"Error parsing window title '{windowTitle}': {ex.Message}");
            }

            return (characterName, serverName);
        }

        private async Task UpdateCharacterListAsync(List<PlayOnlineCharacter> newCharacters)
        {
            if (_disposed) return;

            // Track changes
            var added = new List<PlayOnlineCharacter>();
            var updated = new List<PlayOnlineCharacter>();
            var removed = new List<int>();

            // Find new characters
            foreach (var newChar in newCharacters)
            {
                var existing = _characters.Find(c => c.ProcessId == newChar.ProcessId);
                if (existing == null)
                {
                    _characters.Add(newChar);
                    added.Add(newChar);
                }
                else
                {
                    // Update existing character
                    bool hasChanges = false;
                    
                    if (existing.WindowTitle != newChar.WindowTitle)
                    {
                        existing.WindowTitle = newChar.WindowTitle;
                        hasChanges = true;
                    }
                    
                    if (existing.CharacterName != newChar.CharacterName)
                    {
                        existing.CharacterName = newChar.CharacterName;
                        hasChanges = true;
                    }
                    
                    if (existing.ServerName != newChar.ServerName)
                    {
                        existing.ServerName = newChar.ServerName;
                        hasChanges = true;
                    }
                    
                    if (existing.IsActive != newChar.IsActive)
                    {
                        existing.IsActive = newChar.IsActive;
                        hasChanges = true;
                    }

                    existing.LastSeen = newChar.LastSeen;
                    
                    if (hasChanges)
                    {
                        updated.Add(existing);
                    }
                }
            }

            // Find removed characters
            for (int i = _characters.Count - 1; i >= 0; i--)
            {
                var existing = _characters[i];
                var stillExists = newCharacters.Any(c => c.ProcessId == existing.ProcessId);
                
                if (!stillExists)
                {
                    removed.Add(existing.ProcessId);
                    _characters.RemoveAt(i);
                }
            }

            // Fire events for significant changes only
            foreach (var character in added)
            {
                CharacterDetected?.Invoke(this, character);
                await _loggingService.LogInfoAsync($"New character detected: {character.DisplayName}", "PlayOnlineMonitorService");
            }

            foreach (var character in updated.Where(c => c.IsActive || !string.IsNullOrEmpty(c.CharacterName)))
            {
                CharacterUpdated?.Invoke(this, character);
            }

            foreach (var processId in removed)
            {
                CharacterRemoved?.Invoke(this, processId);
                await _loggingService.LogInfoAsync($"Character removed: Process {processId}", "PlayOnlineMonitorService");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            StopMonitoring();
            _monitoringTimer?.Dispose();
            _activationSemaphore?.Dispose();
            _refreshSemaphore?.Dispose();
        }
    }
}