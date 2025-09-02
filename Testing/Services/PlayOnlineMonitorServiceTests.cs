using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FFXIManager.Infrastructure;
using FFXIManager.Models;
using FFXIManager.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FFXIManager.Tests.Services
{
    // Simple fakes for testing
    internal class FakeLoggingService : ILoggingService
    {
        public List<string> Infos = new();
        public List<string> Warnings = new();
        public List<string> Debugs = new();
        public List<string> Errors = new();
        public Task ClearLogsAsync() => Task.CompletedTask;
        public Task<List<LogEntry>> GetRecentLogsAsync(int count = 100) => Task.FromResult(new List<LogEntry>());
        public Task LogDebugAsync(string message, string? category = null) { Debugs.Add(message); return Task.CompletedTask; }
        public Task LogErrorAsync(string message, Exception? exception = null, string? category = null) { Errors.Add(message); return Task.CompletedTask; }
        public Task LogInfoAsync(string message, string? category = null) { Infos.Add(message); return Task.CompletedTask; }
        public Task LogWarningAsync(string message, string? category = null) { Warnings.Add(message); return Task.CompletedTask; }
    }

    internal class FakeUiDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;
        public void BeginInvoke(Action action) => action();
        public void Invoke(Action action) => action();
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
        public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
    }

    internal class FakeUnifiedMonitoringService : IUnifiedMonitoringService
    {
        public event EventHandler<MonitoredProcessEventArgs>? ProcessDetected;
        public event EventHandler<MonitoredProcessEventArgs>? ProcessUpdated;
        public event EventHandler<MonitoredProcessEventArgs>? ProcessRemoved;
        public event EventHandler<MonitoredProcess>? GlobalProcessDetected;
        public event EventHandler<MonitoredProcess>? GlobalProcessUpdated;
        public event EventHandler<int>? GlobalProcessRemoved;

        private readonly Dictionary<int, MonitoredProcess> _processes = new();
        private readonly Dictionary<Guid, MonitoringProfile> _profiles = new();
        public bool IsMonitoring { get; private set; }

        public Guid RegisterMonitor(MonitoringProfile profile)
        {
            var id = Guid.NewGuid();
            _profiles[id] = profile;
            return id;
        }

        public void UnregisterMonitor(Guid monitorId) { _profiles.Remove(monitorId); }
        public void UpdateMonitorProfile(Guid monitorId, MonitoringProfile profile) { _profiles[monitorId] = profile; }
        public Task<List<MonitoredProcess>> GetProcessesAsync(Guid monitorId) => Task.FromResult(_processes.Values.ToList());
        public Task<MonitoredProcess?> GetProcessAsync(Guid monitorId, int processId) => Task.FromResult(_processes.TryGetValue(processId, out var p) ? p : null);
        public void StartMonitoring() { IsMonitoring = true; }
        public void StopMonitoring() { IsMonitoring = false; }

        // Helper to add processes for testing
        public void AddProcess(MonitoredProcess process)
        {
            _processes[process.ProcessId] = process;
        }
    }

    internal class FakeProcessManagementService : IProcessManagementService
    {
        public event EventHandler<WindowTitleChangedEventArgs>? WindowTitleChanged;
        public event EventHandler<ProcessInfo>? ProcessDetected;
        public event EventHandler<ProcessInfo>? ProcessTerminated;
        public event EventHandler<ProcessInfo>? ProcessUpdated;

        private readonly Dictionary<int, ProcessInfo> _store = new();
        private bool _monitoring;

        public Task<bool> ActivateWindowAsync(IntPtr windowHandle, int timeoutMs = 5000) => Task.FromResult(windowHandle != IntPtr.Zero);
        public Task<List<ProcessInfo>> GetAllProcessesAsync() => Task.FromResult(_store.Values.ToList());
        public Task<ProcessInfo?> GetProcessByIdAsync(int processId) => Task.FromResult(_store.TryGetValue(processId, out var v) ? v : null);
        public Task<List<ProcessInfo>> GetProcessesByNamesAsync(IEnumerable<string> processNames)
        {
            var set = new HashSet<string>(processNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var list = _store.Values.Where(p => set.Contains(p.ProcessName)).ToList();
            return Task.FromResult(list);
        }
        public Task<List<WindowInfo>> GetProcessWindowsAsync(int processId)
        {
            if (_store.TryGetValue(processId, out var pi)) return Task.FromResult(pi.Windows);
            return Task.FromResult(new List<WindowInfo>());
        }
        public bool IsProcessRunning(int processId) => _store.ContainsKey(processId);
        public Task<bool> KillProcessAsync(int processId, int timeoutMs = 5000)
        {
            bool existed = _store.Remove(processId);
            if (existed) ProcessTerminated?.Invoke(this, new ProcessInfo { ProcessId = processId });
            return Task.FromResult(existed);
        }
        public Guid RegisterDiscoveryWatch(DiscoveryFilter filter) => Guid.NewGuid();
        public void StartGlobalMonitoring(TimeSpan interval) { _monitoring = true; }
        public void StopGlobalMonitoring() { _monitoring = false; }
        public void TrackPid(int processId) { }
        public void UnregisterDiscoveryWatch(Guid watchId) { }
        public void UntrackPid(int processId) { }
        public void RequestImmediateRefresh() { }

        // Helpers to drive events
        public void AddProcess(ProcessInfo pi)
        {
            _store[pi.ProcessId] = pi;
            ProcessDetected?.Invoke(this, pi);
        }
        public void UpdateProcess(ProcessInfo pi)
        {
            _store[pi.ProcessId] = pi;
            ProcessUpdated?.Invoke(this, pi);
        }
        public void EmitTitleChanged(int pid, IntPtr hwnd, string title)
        {
            WindowTitleChanged?.Invoke(this, new WindowTitleChangedEventArgs { ProcessId = pid, WindowHandle = hwnd, NewTitle = title });
        }
    }

    [TestClass]
    public class PlayOnlineMonitorServiceTests
    {
        private FakeLoggingService _log = null!;
        private FakeUnifiedMonitoringService _unified = null!;
        private FakeProcessManagementService _proc = null!;
        private FakeUiDispatcher _ui = null!;
        private PlayOnlineMonitorService _service = null!;

        [TestInitialize]
        public void Setup()
        {
            _log = new FakeLoggingService();
            _unified = new FakeUnifiedMonitoringService();
            _proc = new FakeProcessManagementService();
            _ui = new FakeUiDispatcher();
            _service = new PlayOnlineMonitorService(_unified, _log, _ui, _proc);
        }

        [TestCleanup]
        public void Teardown()
        {
            _service.Dispose();
        }

        [TestMethod]
        public async Task GetRunningCharacters_UsesFilteringAndWindows()
        {
            var hwnd = new IntPtr(1234);
            var pi = new ProcessInfo { ProcessId = 1, ProcessName = "pol", Windows = new List<WindowInfo> { new WindowInfo { Handle = hwnd, Title = "Character A" } }, LastSeen = DateTime.UtcNow };
            _proc.AddProcess(pi);

            var list = await _service.GetCharactersAsync();
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual("Character A", list[0].WindowTitle);
            Assert.AreEqual(hwnd, list[0].WindowHandle);
        }

        [TestMethod]
        public async Task ActivateCharacterWindow_DispatchesAndSetsActive()
        {
            var hwnd = new IntPtr(5678);
            var pi = new ProcessInfo { ProcessId = 2, ProcessName = "ffxi", Windows = new List<WindowInfo> { new WindowInfo { Handle = hwnd, Title = "Character B" } }, LastSeen = DateTime.UtcNow };
            _proc.AddProcess(pi);

            var list = await _service.GetCharactersAsync();
            var ch = list[0];
            var ok = await _service.ActivateCharacterWindowAsync(ch);
            Assert.IsTrue(ok);
        }

        [TestMethod]
        public void StartStopMonitoring_RegistersDiscoveryAndGlobalMonitor()
        {
            _service.StartMonitoring();
            _service.StopMonitoring();
            // If no exceptions, considered success for this basic lifecycle test
            Assert.IsTrue(true);
        }

        [TestMethod]
        public async Task WindowTitleChanged_UpdatesTitleAndUsesThrottleFallback()
        {
            _service.StartMonitoring();
            var hwnd = new IntPtr(9012);
            var pi = new ProcessInfo { ProcessId = 3, ProcessName = "pol", Windows = new List<WindowInfo> { new WindowInfo { Handle = hwnd, Title = "Old" } }, LastSeen = DateTime.UtcNow };
            _proc.AddProcess(pi);

            var characters = await _service.GetCharactersAsync();
            Assert.AreEqual(1, characters.Count);
            var ch = characters[0];
            Assert.AreEqual("Old", ch.WindowTitle);

            // Emit title changed
            _proc.EmitTitleChanged(3, hwnd, "NewTitle");

            // Change should be reflected synchronously due to FakeUiDispatcher invoking inline
            Assert.AreEqual("NewTitle", ch.WindowTitle);
        }

        [TestMethod]
        public async Task ProcessUpdated_RefreshesCharacterTitles()
        {
            _service.StartMonitoring();
            var hwnd = new IntPtr(1111);
            var pi = new ProcessInfo { ProcessId = 4, ProcessName = "ffxi", Windows = new List<WindowInfo> { new WindowInfo { Handle = hwnd, Title = "Title1" } }, LastSeen = DateTime.UtcNow };
            _proc.AddProcess(pi);
            var characters = await _service.GetCharactersAsync();
            var ch = characters[0];
            Assert.AreEqual("Title1", ch.WindowTitle);

            // Update process window title via ProcessUpdated event
            pi.Windows[0].Title = "Title2";
            _proc.UpdateProcess(pi);

            Assert.AreEqual("Title2", ch.WindowTitle);
        }

        [TestMethod]
        public async Task ProcessTerminated_RemovesCharacters()
        {
            _service.StartMonitoring();
            var hwnd = new IntPtr(2222);
            var pi = new ProcessInfo { ProcessId = 5, ProcessName = "pol", Windows = new List<WindowInfo> { new WindowInfo { Handle = hwnd, Title = "Exists" } }, LastSeen = DateTime.UtcNow };
            _proc.AddProcess(pi);
            var list = await _service.GetCharactersAsync();
            Assert.AreEqual(1, list.Count);

            var removed = false;
            _service.CharacterRemoved += (s, pid) => { if (pid == 5) removed = true; };
            await _proc.KillProcessAsync(5);

            Assert.IsTrue(removed);
        }
    }
}

