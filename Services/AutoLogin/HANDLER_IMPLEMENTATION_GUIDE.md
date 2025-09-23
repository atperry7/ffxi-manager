# Handler Implementation Guide
## Developer Guidelines for Auto-Login Task/Subtask Implementation

This guide provides comprehensive patterns and best practices for implementing real automation logic in the auto-login handlers.

## 🎯 Core Principles

### 1. Always Use QueueExecutionState
- **Never bypass the state machine** for decision making
- Check `task.Status` before proceeding with operations
- Respect pause states and wait appropriately
- Use the established state transitions

```csharp
// ✅ CORRECT: Respect pause state
while (task.Status == AutoLoginTaskStatus.Paused && !cancellationToken.IsCancellationRequested)
{
    await Task.Delay(100, cancellationToken);
}
cancellationToken.ThrowIfCancellationRequested();

// ❌ INCORRECT: Ignoring state machine
// Just proceeding without checking task status
```

### 2. Respect Cancellation Tokens
- **Check `cancellationToken.IsCancellationRequested`** at every async boundary
- Use `cancellationToken` in all async operations
- Handle `OperationCanceledException` appropriately

```csharp
// ✅ CORRECT: Proper cancellation handling
for (int attempt = 1; attempt <= maxRetries; attempt++)
{
    cancellationToken.ThrowIfCancellationRequested();

    var result = await SomeAsyncOperation(cancellationToken);
    if (result.Success) break;

    await Task.Delay(retryDelay, cancellationToken);
}

// ❌ INCORRECT: Not using cancellation token
// await Task.Delay(retryDelay); // Missing cancellation token
```

### 3. Fire Granular Events
- **Each subtask state change** should trigger UI updates via events
- Use `subtask.UpdateProgress()` frequently with meaningful messages
- Provide detailed progress for long-running operations

```csharp
// ✅ CORRECT: Granular progress reporting
subtask.UpdateProgress(25, "Locating application window...");
await Task.Delay(500, cancellationToken);

subtask.UpdateProgress(50, "Verifying window is responsive...");
await Task.Delay(300, cancellationToken);

subtask.UpdateProgress(75, "Focusing window for interaction...");
// Actual UI automation here

subtask.UpdateProgress(100, "Window ready for automation");
```

### 4. Keep Subtasks Focused
- **Single responsibility per subtask** (launch app, enter password, etc.)
- Don't mix concerns within a single subtask execution
- If logic becomes complex, consider breaking into additional subtasks

### 5. Leverage Existing Patterns
- Use the same event/progress reporting patterns established
- Follow dependency injection patterns
- Build on the solid state management foundation

## 🏗️ Implementation Strategy

### Strategy Pattern for Different Workflows

Implement different strategies for various login scenarios:

```csharp
// Example: Strategy pattern for different login workflows
public interface ILoginWorkflowStrategy
{
    bool CanHandle(AutoLoginQueueItem queueItem);
    List<AutoLoginSubtask> GetRequiredSubtasks(AutoLoginQueueItem queueItem);
}

public class POLOnlyWorkflowStrategy : ILoginWorkflowStrategy
{
    public bool CanHandle(AutoLoginQueueItem queueItem)
    {
        // Logic to determine if this is POL-only workflow
        return queueItem.Account?.RequiresPOLOnly ?? false;
    }

    public List<AutoLoginSubtask> GetRequiredSubtasks(AutoLoginQueueItem queueItem)
    {
        return new List<AutoLoginSubtask>
        {
            new AutoLoginSubtask { TaskStep = LoginTaskStep.LaunchWindower },
            new AutoLoginSubtask { TaskStep = LoginTaskStep.MemberSelection },
            new AutoLoginSubtask { TaskStep = LoginTaskStep.PasswordEntry }
            // Skip FFXI-specific steps
        };
    }
}
```

### Independent Cancellable Subtasks

Each subtask should be independently testable and cancellable:

```csharp
public async Task ExecuteSubtaskAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, CancellationToken cancellationToken)
{
    try
    {
        // Setup with timeout for this specific subtask
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(SubtaskTimeoutSeconds));
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        // Execute with combined cancellation
        await ExecuteSubtaskLogic(subtask, queueItem, combinedCts.Token);
    }
    catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
    {
        // Handle timeout specifically
        throw new TimeoutException($"Subtask {subtask.TaskStep} timed out");
    }
}
```

## 🛠️ Handler Implementation Patterns

### 1. Window Detection and Management

```csharp
private async Task<IntPtr> FindApplicationWindowAsync(string windowTitle, CancellationToken cancellationToken)
{
    const int maxAttempts = 10;
    const int delayBetweenAttempts = 500;

    for (int attempt = 1; attempt <= maxAttempts; attempt++)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var windowHandle = FindWindow(null, windowTitle);
        if (windowHandle != IntPtr.Zero)
        {
            await _loggingService.LogDebugAsync($"Found window '{windowTitle}' on attempt {attempt}");
            return windowHandle;
        }

        if (attempt < maxAttempts)
        {
            await Task.Delay(delayBetweenAttempts, cancellationToken);
        }
    }

    throw new InvalidOperationException($"Could not find window '{windowTitle}' after {maxAttempts} attempts");
}
```

### 2. UI Element Interaction

```csharp
private async Task ClickButtonAsync(IntPtr windowHandle, string buttonText, CancellationToken cancellationToken)
{
    subtask.UpdateProgress(20, $"Locating '{buttonText}' button...");

    // Find the button element
    var buttonElement = await FindUIElementAsync(windowHandle, buttonText, cancellationToken);

    subtask.UpdateProgress(40, "Verifying button is clickable...");

    // Verify button state
    if (!IsElementEnabled(buttonElement))
    {
        throw new InvalidOperationException($"Button '{buttonText}' is not enabled");
    }

    subtask.UpdateProgress(60, "Clicking button...");

    // Perform the click
    await ClickElementAsync(buttonElement, cancellationToken);

    subtask.UpdateProgress(80, "Waiting for button click response...");

    // Wait for expected response/window change
    await WaitForExpectedStateChange(cancellationToken);

    subtask.UpdateProgress(100, $"'{buttonText}' button clicked successfully");
}
```

### 3. Text Input with Security

```csharp
private async Task EnterSecureTextAsync(IntPtr windowHandle, string fieldName, string text, CancellationToken cancellationToken)
{
    subtask.UpdateProgress(25, $"Locating {fieldName} field...");

    var textField = await FindUIElementAsync(windowHandle, fieldName, cancellationToken);

    subtask.UpdateProgress(50, "Focusing input field...");

    // Focus the field
    await FocusElementAsync(textField, cancellationToken);

    subtask.UpdateProgress(75, "Entering text securely...");

    // Clear existing content and enter new text
    await ClearFieldAsync(textField, cancellationToken);
    await SendKeysAsync(text, cancellationToken);

    // Don't log the actual text for security
    await _loggingService.LogDebugAsync($"{fieldName} field populated (content masked for security)");

    subtask.UpdateProgress(100, $"{fieldName} entered successfully");
}
```

### 4. Process Management

```csharp
private async Task LaunchApplicationAsync(string executablePath, string arguments, CancellationToken cancellationToken)
{
    subtask.UpdateProgress(10, "Validating executable path...");

    if (!File.Exists(executablePath))
    {
        throw new FileNotFoundException($"Executable not found: {executablePath}");
    }

    subtask.UpdateProgress(30, "Starting application process...");

    var processStartInfo = new ProcessStartInfo
    {
        FileName = executablePath,
        Arguments = arguments,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    var process = Process.Start(processStartInfo);
    if (process == null)
    {
        throw new InvalidOperationException($"Failed to start process: {executablePath}");
    }

    subtask.UpdateProgress(60, $"Process started with PID: {process.Id}");

    // Wait for process to initialize
    await WaitForProcessInitialization(process, cancellationToken);

    subtask.UpdateProgress(100, "Application launched successfully");

    // Store process reference for monitoring
    // _processTracker.TrackProcess(process);
}
```

## 🧪 Testing Patterns

### Unit Testing Individual Handlers

```csharp
[TestClass]
public class WindowerLaunchHandlerTests
{
    private Mock<ILoggingService> _mockLoggingService;
    private Mock<IProcessUtilityService> _mockProcessService;
    private WindowerLaunchHandler _handler;

    [TestInitialize]
    public void Setup()
    {
        _mockLoggingService = new Mock<ILoggingService>();
        _mockProcessService = new Mock<IProcessUtilityService>();
        _handler = new WindowerLaunchHandler(_mockLoggingService.Object, _mockProcessService.Object);
    }

    [TestMethod]
    public async Task ExecuteLaunchWindowerAsync_ValidAccount_LaunchesSuccessfully()
    {
        // Arrange
        var queueItem = CreateTestQueueItem();
        var subtask = new AutoLoginSubtask { TaskStep = LoginTaskStep.LaunchWindower };
        var cancellationToken = CancellationToken.None;

        // Act
        await _handler.ExecuteAsync(subtask, queueItem, cancellationToken);

        // Assert
        Assert.AreEqual(AutoLoginSubtaskStatus.Completed, subtask.Status);
        Assert.AreEqual(100, subtask.Progress);
    }
}
```

### Integration Testing with Real UI

```csharp
[TestClass]
[TestCategory("Integration")]
public class PlayOnlineAuthHandlerIntegrationTests
{
    [TestMethod]
    [Ignore("Requires PlayOnline to be running")]
    public async Task ExecuteMemberSelectionAsync_RealUI_SelectsMemberSuccessfully()
    {
        // This test would interact with actual PlayOnline UI
        // Run only in controlled test environments
    }
}
```

## 🔧 Configuration and Settings

### Handler-Specific Settings

```csharp
public class AutoLoginHandlerSettings
{
    public int DefaultTimeoutSeconds { get; set; } = 30;
    public int RetryAttempts { get; set; } = 3;
    public int DelayBetweenRetries { get; set; } = 1000;
    public bool EnableDetailedLogging { get; set; } = true;
    public string WindowerExecutablePath { get; set; } = "";
    public Dictionary<string, object> HandlerSpecificSettings { get; set; } = new();
}
```

### Environment-Specific Behavior

```csharp
public class HandlerEnvironmentAdapter
{
    public static bool IsRunningInTestMode =>
        Environment.GetEnvironmentVariable("FFXI_HANDLER_TEST_MODE") == "true";

    public static bool ShouldUseSimulation =>
        IsRunningInTestMode || !HasRequiredDependencies();

    private static bool HasRequiredDependencies()
    {
        // Check if required applications/services are available
        return File.Exists(GetWindowerPath()) && IsPlayOnlineInstalled();
    }
}
```

## 🚨 Error Handling and Recovery

### Graceful Degradation

```csharp
public async Task ExecuteWithFallbackAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, CancellationToken cancellationToken)
{
    try
    {
        // Primary automation approach
        await ExecutePrimaryAutomationAsync(subtask, queueItem, cancellationToken);
    }
    catch (AutomationException ex) when (ex.IsRecoverable)
    {
        await _loggingService.LogWarningAsync($"Primary automation failed, attempting fallback: {ex.Message}");

        // Fallback approach
        await ExecuteFallbackAutomationAsync(subtask, queueItem, cancellationToken);
    }
    catch (Exception ex)
    {
        await _loggingService.LogErrorAsync($"All automation approaches failed for {subtask.TaskStep}", ex);
        throw;
    }
}
```

### Retry Logic with Exponential Backoff

```csharp
private async Task<T> ExecuteWithRetryAsync<T>(
    Func<CancellationToken, Task<T>> operation,
    int maxRetries = 3,
    CancellationToken cancellationToken = default)
{
    var delay = TimeSpan.FromMilliseconds(500);

    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            return await operation(cancellationToken);
        }
        catch (Exception ex) when (attempt < maxRetries && IsRetryableException(ex))
        {
            await _loggingService.LogWarningAsync($"Attempt {attempt} failed, retrying in {delay.TotalMilliseconds}ms: {ex.Message}");
            await Task.Delay(delay, cancellationToken);
            delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 1.5); // Exponential backoff
        }
    }

    // This will never be reached due to the exception handling above, but satisfies compiler
    throw new InvalidOperationException("Retry logic failed unexpectedly");
}
```

## 📋 Implementation Checklist

When implementing a new handler or enhancing existing ones:

### ✅ Before Writing Code
- [ ] Review the existing handler pattern and interfaces
- [ ] Understand the specific subtask responsibilities
- [ ] Check what properties are available on `PlayOnlineMemberAccount`
- [ ] Plan the UI automation approach (WinAPI, UI Automation, etc.)

### ✅ During Implementation
- [ ] Use `cancellationToken` in all async operations
- [ ] Implement granular progress reporting with `subtask.UpdateProgress()`
- [ ] Add comprehensive logging for debugging
- [ ] Handle both success and failure scenarios
- [ ] Respect pause state in long-running operations

### ✅ After Implementation
- [ ] Write unit tests for the handler logic
- [ ] Test with actual UI if possible (integration tests)
- [ ] Verify progress reporting works in the UI
- [ ] Test pause/resume/skip functionality
- [ ] Update handler documentation

### ✅ Production Readiness
- [ ] Add error handling and recovery mechanisms
- [ ] Implement retry logic where appropriate
- [ ] Add configuration options for timeouts and retries
- [ ] Ensure security best practices (no logging of sensitive data)
- [ ] Performance testing under various system conditions

## 🔄 Continuous Improvement

### Monitoring and Metrics

Consider adding handler-specific metrics:

```csharp
public class HandlerMetrics
{
    public string HandlerName { get; set; }
    public LoginTaskStep TaskStep { get; set; }
    public TimeSpan ExecutionTime { get; set; }
    public bool Successful { get; set; }
    public int RetryCount { get; set; }
    public string ErrorMessage { get; set; }
}
```

### Feedback Loop

Implement feedback mechanisms to improve automation reliability:

```csharp
public async Task ReportHandlerPerformanceAsync(HandlerMetrics metrics)
{
    // Log performance data for analysis
    await _loggingService.LogInfoAsync($"Handler {metrics.HandlerName} executed in {metrics.ExecutionTime.TotalMilliseconds}ms, Success: {metrics.Successful}");

    // Could send to analytics service for pattern analysis
    // await _analyticsService.TrackHandlerPerformance(metrics);
}
```

---

## 🎯 Remember

The goal is to build reliable, maintainable automation that respects the existing architecture and provides a smooth user experience. Start with the most critical path (Windower launch) and gradually enhance each handler while maintaining the solid foundation we've established.

Each handler should be a focused, testable unit that can evolve independently while participating in the larger auto-login workflow orchestration.