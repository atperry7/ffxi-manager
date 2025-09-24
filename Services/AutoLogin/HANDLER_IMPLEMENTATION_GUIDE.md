# Handler Implementation Guide
## Developer Guidelines for Auto-Login Handler Development

> **📝 Claude Code Users**: See [`CLAUDE_HANDLER_GUIDE.md`](./CLAUDE_HANDLER_GUIDE.md) for a streamlined, quick-reference version optimized for AI-assisted development.

This guide provides comprehensive patterns and best practices for implementing auto-login handlers that integrate with the refactored AutoLoginTask/AutoLoginSubtask architecture.

## 🎯 Core Architecture Principles

### 1. Handler Scope and Responsibility
Handlers operate on **individual AutoLoginSubtask objects** within the context of an AutoLoginTask:

```csharp
// ✅ CORRECT: Handler signature for AutoLoginSubtask
public async Task ExecuteAsync(
    AutoLoginSubtask subtask,
    AutoLoginQueueItem queueItem,
    CancellationToken cancellationToken)
{
    // Handler implementation
}
```

### 2. Subtask Lifecycle Management
- **Always use subtask lifecycle methods** for proper state tracking
- Leverage the built-in status management and event propagation
- Use the established AutoLoginSubtaskStatus transitions

```csharp
// ✅ CORRECT: Proper subtask lifecycle
subtask.Start(); // Sets status to InProgress, records StartTime
try
{
    // Handler logic here
    subtask.UpdateProgress(50, "Processing step...");
    // More logic
    subtask.Complete(); // Sets status to Completed, records EndTime
}
catch (Exception ex)
{
    subtask.Fail(ex.Message); // Sets status to Failed, records error
    throw;
}

// ❌ INCORRECT: Manual status manipulation
// subtask.Status = AutoLoginSubtaskStatus.InProgress; // Don't do this
```

### 3. Handle Optional and Skippable Steps
Leverage subtask properties for intelligent execution flow:

```csharp
// ✅ CORRECT: Handle skippable subtasks
public async Task ExecuteAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, CancellationToken cancellationToken)
{
    subtask.Start();

    // Check if this step can be skipped based on conditions
    if (subtask.IsSkippable && !IsStepRequired(queueItem))
    {
        subtask.Skip("Step not required for this account type");
        return;
    }

    // Proceed with normal execution
    await ExecuteStepLogic(subtask, queueItem, cancellationToken);
}
```

### 4. Implement Robust Retry Logic
Use the built-in retry capabilities of AutoLoginSubtask:

```csharp
// ✅ CORRECT: Leverage built-in retry mechanism
public async Task ExecuteWithRetryAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, CancellationToken cancellationToken)
{
    while (true)
    {
        try
        {
            await ExecuteStepLogic(subtask, queueItem, cancellationToken);
            break; // Success, exit retry loop
        }
        catch (Exception ex) when (subtask.CanRetry)
        {
            subtask.PrepareForRetry(); // Increments RetryAttempts, resets status
            await Task.Delay(GetRetryDelay(subtask.RetryAttempts), cancellationToken);
            continue;
        }
        catch (Exception ex)
        {
            subtask.Fail($"Failed after {subtask.RetryAttempts} attempts: {ex.Message}");
            throw;
        }
    }
}
```

### 5. Respect Cancellation Tokens
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

    await Task.Delay(GetRetryDelay(attempt), cancellationToken);
}

// ❌ INCORRECT: Not using cancellation token
// await Task.Delay(retryDelay); // Missing cancellation token
```

### 6. Provide Granular Progress Updates
- **Use `subtask.UpdateProgress()`** frequently with meaningful messages
- Progress updates automatically trigger UI events through the task executor
- Provide detailed progress for long-running operations

```csharp
// ✅ CORRECT: Granular progress reporting with automatic event propagation
subtask.UpdateProgress(25, "Locating application window...");
await Task.Delay(500, cancellationToken);

subtask.UpdateProgress(50, "Verifying window is responsive...");
await Task.Delay(300, cancellationToken);

subtask.UpdateProgress(75, "Focusing window for interaction...");
// Actual UI automation here

subtask.UpdateProgress(100, "Window ready for automation");
// Progress updates automatically flow through: Subtask → Task → TaskExecutor → Orchestrator → UI
```

### 7. Keep Handlers Focused
- **Single responsibility per handler** (one LoginTaskStep per handler)
- Don't mix concerns within a single handler execution
- Use dependency injection for service access

### 8. Leverage Service Architecture
The refactored system provides these key services through dependency injection:

```csharp
// ✅ CORRECT: Handler with proper service dependencies
public class WindowLaunchHandler : IAutoLoginStepHandler
{
    private readonly ILoggingService _loggingService;
    private readonly IProcessUtilityService _processService;
    private readonly ISettingsService _settingsService;

    public WindowLaunchHandler(
        ILoggingService loggingService,
        IProcessUtilityService processService,
        ISettingsService settingsService)
    {
        _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public LoginTaskStep HandledStep => LoginTaskStep.LaunchWindower;

    public async Task ExecuteAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, CancellationToken cancellationToken)
    {
        subtask.Start();

        try
        {
            // Use estimated duration from subtask for timeout calculations
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(subtask.EstimatedDurationSeconds * 2));
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            await ExecuteStepLogic(subtask, queueItem, combinedCts.Token);
            subtask.Complete();
        }
        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
        {
            subtask.Fail($"Step timed out after {subtask.EstimatedDurationSeconds * 2} seconds");
            throw new TimeoutException($"Handler {HandledStep} timed out");
        }
        catch (Exception ex)
        {
            subtask.Fail(ex.Message);
            throw;
        }
    }
}
```

## 🏗️ Service Integration Patterns

### Working with the Task Executor
Handlers are invoked by the `IAutoLoginTaskExecutor` which manages the task/subtask lifecycle:

```csharp
// The TaskExecutor handles:
// 1. Calling subtask.Start() before invoking handler
// 2. Setting up cancellation tokens with timeouts
// 3. Propagating events (SubtaskStarted, SubtaskProgressUpdated, SubtaskCompleted)
// 4. Managing retry logic when handlers fail
// 5. Calling subtask.Complete() or subtask.Fail() based on handler results
```

### Event Flow Architecture
The refactored system uses a layered event propagation model:

```
AutoLoginSubtask (progress updates)
    ↓ (PropertyChanged events)
AutoLoginTask (aggregates subtask progress)
    ↓ (TaskExecutor events)
IAutoLoginTaskExecutor (SubtaskStarted, SubtaskProgressUpdated, etc.)
    ↓ (Event forwarding)
IQueueExecutionOrchestrator (ItemStarted, ItemProgressUpdated, etc.)
    ↓ (Event forwarding)
AutoLoginQueueService (Facade events for UI)
    ↓ (Event binding)
UI ViewModels (Data binding updates)
```

### Handler Registration Pattern
Handlers should be registered in the DI container by LoginTaskStep:

```csharp
// In Infrastructure/DependencyInjection.cs
services.AddTransient<IAutoLoginStepHandler, LaunchWindowerHandler>();
services.AddTransient<IAutoLoginStepHandler, MemberSelectionHandler>();
services.AddTransient<IAutoLoginStepHandler, PasswordEntryHandler>();
// ... etc

// Handler resolver service
services.AddSingleton<IAutoLoginHandlerResolver, AutoLoginHandlerResolver>();
```

## 🛠️ Handler Implementation Patterns

### 1. Window Detection and Management
Modern pattern with AutoLoginSubtask integration:

```csharp
private async Task<IntPtr> FindApplicationWindowAsync(
    AutoLoginSubtask subtask,
    string windowTitle,
    CancellationToken cancellationToken)
{
    const int maxAttempts = 10;
    const int delayBetweenAttempts = 500;

    for (int attempt = 1; attempt <= maxAttempts; attempt++)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Update progress based on attempt
        var progressPercent = (attempt * 100) / maxAttempts;
        subtask.UpdateProgress(progressPercent, $"Searching for window '{windowTitle}' (attempt {attempt}/{maxAttempts})");

        var windowHandle = FindWindow(null, windowTitle);
        if (windowHandle != IntPtr.Zero)
        {
            await _loggingService.LogDebugAsync($"Found window '{windowTitle}' on attempt {attempt}");
            subtask.UpdateProgress(100, $"Found window '{windowTitle}'");
            return windowHandle;
        }

        if (attempt < maxAttempts)
        {
            await Task.Delay(delayBetweenAttempts, cancellationToken);
        }
    }

    var errorMsg = $"Could not find window '{windowTitle}' after {maxAttempts} attempts";
    subtask.Fail(errorMsg);
    throw new InvalidOperationException(errorMsg);
}
```

## 🤖 Claude Code Development Guidelines

### For Claude Code: Handler Creation Workflow

When creating new auto-login handlers, follow this systematic approach:

#### 1. **Analyze the LoginTaskStep**
```csharp
// First, understand what LoginTaskStep you're implementing
// Check Models/LoginTaskStep.cs for the step definition
// Example: LoginTaskStep.LaunchWindower, LoginTaskStep.MemberSelection, etc.
```

#### 2. **Handler Template Pattern**
Use this template for new handlers:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Services;

namespace FFXIManager.Services.AutoLogin.Handlers
{
    /// <summary>
    /// Handler for [LoginTaskStep] - [Brief description of what this handler does]
    /// </summary>
    public class [StepName]Handler : IAutoLoginStepHandler
    {
        private readonly ILoggingService _loggingService;
        // Add other required services

        public [StepName]Handler(ILoggingService loggingService /* other services */)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            // Initialize other services with null checks
        }

        public LoginTaskStep HandledStep => LoginTaskStep.[YourStep];

        public async Task ExecuteAsync(
            AutoLoginSubtask subtask,
            AutoLoginQueueItem queueItem,
            CancellationToken cancellationToken)
        {
            subtask.Start();

            try
            {
                // Check if step can be skipped
                if (subtask.IsSkippable && ShouldSkipStep(queueItem))
                {
                    subtask.Skip("Reason for skipping");
                    return;
                }

                // Set up timeout based on estimated duration
                using var timeoutCts = new CancellationTokenSource(
                    TimeSpan.FromSeconds(subtask.EstimatedDurationSeconds * 2));
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, timeoutCts.Token);

                // Execute the main logic
                await ExecuteStepLogicAsync(subtask, queueItem, combinedCts.Token);

                subtask.Complete();
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
            {
                subtask.Fail($"Handler timed out after {subtask.EstimatedDurationSeconds * 2} seconds");
                throw new TimeoutException($"Handler {HandledStep} timed out");
            }
            catch (Exception ex)
            {
                subtask.Fail(ex.Message);
                await _loggingService.LogErrorAsync($"Handler {HandledStep} failed", ex);
                throw;
            }
        }

        private async Task ExecuteStepLogicAsync(
            AutoLoginSubtask subtask,
            AutoLoginQueueItem queueItem,
            CancellationToken cancellationToken)
        {
            // Implement your specific logic here
            // Use subtask.UpdateProgress(percentage, message) frequently
            // Access account info via queueItem.Account
            // Use cancellationToken in all async operations
        }

        private bool ShouldSkipStep(AutoLoginQueueItem queueItem)
        {
            // Implement logic to determine if this step should be skipped
            // based on account configuration or other factors
            return false;
        }
    }
}
```

#### 3. **Testing Pattern for Claude Code**
Create corresponding tests using this pattern:

```csharp
[TestClass]
public class [StepName]HandlerTests
{
    private Mock<ILoggingService> _mockLoggingService;
    private [StepName]Handler _handler;

    [TestInitialize]
    public void Setup()
    {
        _mockLoggingService = new Mock<ILoggingService>();
        _handler = new [StepName]Handler(_mockLoggingService.Object);
    }

    [TestMethod]
    public async Task ExecuteAsync_ValidInput_CompletesSuccessfully()
    {
        // Arrange
        var subtask = AutoLoginSubtask.FromLoginTaskStep(LoginTaskStep.[YourStep]);
        var queueItem = CreateTestQueueItem();
        var cancellationToken = CancellationToken.None;

        // Act
        await _handler.ExecuteAsync(subtask, queueItem, cancellationToken);

        // Assert
        Assert.AreEqual(AutoLoginSubtaskStatus.Completed, subtask.Status);
        Assert.AreEqual(100, subtask.Progress);
    }

    private AutoLoginQueueItem CreateTestQueueItem()
    {
        // Create test data - use existing patterns from other test files
        return new AutoLoginQueueItem(/* test parameters */);
    }
}
```

#### 4. **Registration Pattern**
Add your handler to the DI container in `Infrastructure/DependencyInjection.cs`:

```csharp
// In the AddAutoLoginServices method
services.AddTransient<IAutoLoginStepHandler, [StepName]Handler>();
```

### Claude Code: Common Implementation Patterns

#### UI Element Interaction with Subtask Integration
```csharp
private async Task ClickButtonAsync(
    AutoLoginSubtask subtask,
    IntPtr windowHandle,
    string buttonText,
    CancellationToken cancellationToken)
{
    subtask.UpdateProgress(20, $"Locating '{buttonText}' button...");

    var buttonElement = await FindUIElementAsync(windowHandle, buttonText, cancellationToken);

    subtask.UpdateProgress(40, "Verifying button is clickable...");

    if (!IsElementEnabled(buttonElement))
    {
        throw new InvalidOperationException($"Button '{buttonText}' is not enabled");
    }

    subtask.UpdateProgress(60, "Clicking button...");
    await ClickElementAsync(buttonElement, cancellationToken);

    subtask.UpdateProgress(80, "Waiting for button click response...");
    await WaitForExpectedStateChange(cancellationToken);

    subtask.UpdateProgress(100, $"'{buttonText}' button clicked successfully");
}
```

#### Secure Text Input with AutoLoginSubtask
```csharp
private async Task EnterSecureTextAsync(
    AutoLoginSubtask subtask,
    IntPtr windowHandle,
    string fieldName,
    string text,
    CancellationToken cancellationToken)
{
    subtask.UpdateProgress(25, $"Locating {fieldName} field...");

    var textField = await FindUIElementAsync(windowHandle, fieldName, cancellationToken);

    subtask.UpdateProgress(50, "Focusing input field...");
    await FocusElementAsync(textField, cancellationToken);

    subtask.UpdateProgress(75, "Entering text securely...");
    await ClearFieldAsync(textField, cancellationToken);
    await SendKeysAsync(text, cancellationToken);

    // SECURITY: Never log sensitive content
    await _loggingService.LogDebugAsync($"{fieldName} field populated (content masked for security)");

    subtask.UpdateProgress(100, $"{fieldName} entered successfully");
}
```

### Claude Code: Key Implementation Guidelines

#### ✅ **DO** - Best Practices
- **Always call `subtask.Start()`** at the beginning of handler execution
- **Use `subtask.UpdateProgress()`** frequently with meaningful messages (every 20-25% progress)
- **Call `subtask.Complete()`** on successful completion or `subtask.Fail(message)` on error
- **Leverage `subtask.IsSkippable`** to handle optional steps gracefully
- **Use `subtask.EstimatedDurationSeconds`** for timeout calculations
- **Pass `AutoLoginSubtask` to all helper methods** for progress tracking
- **Check account properties via `queueItem.Account`** for step-specific logic
- **Use dependency injection** for all services (ILoggingService, IProcessUtilityService, etc.)
- **Follow the exact handler template** provided above for consistency

#### ❌ **DON'T** - Anti-Patterns
- **Don't manually set `subtask.Status`** - use lifecycle methods instead
- **Don't bypass progress reporting** - UI depends on granular updates
- **Don't forget cancellation token** in async operations
- **Don't log sensitive information** (passwords, tokens, personal data)
- **Don't create handlers for multiple LoginTaskSteps** - one handler per step
- **Don't catch exceptions without calling `subtask.Fail()`** first
- **Don't skip the DI registration** step in Infrastructure/DependencyInjection.cs

#### 🧪 **Testing Requirements**
- **Create MSTest test class** for each handler
- **Test happy path** (successful execution)
- **Test error scenarios** (timeouts, failures)
- **Test skippable logic** if `IsSkippable = true`
- **Test cancellation handling**
- **Mock all external dependencies**

### File Location Patterns for Claude Code
```
Services/AutoLogin/Handlers/[StepName]Handler.cs          # Implementation
Testing/Services/AutoLogin/Handlers/[StepName]HandlerTests.cs  # Tests
Infrastructure/DependencyInjection.cs                     # Registration
```

---

## 🎯 Quick Reference for Claude Code

**When asked to create an auto-login handler:**

1. **Read** `Models/LoginTaskStep.cs` to understand the step
2. **Use** the handler template provided above
3. **Implement** `ExecuteStepLogicAsync` with proper progress tracking
4. **Create** corresponding test file
5. **Register** in DI container
6. **Build** and test with `dotnet build` and `dotnet test`

---

## 🖼️ Screenshot Detection Integration

### Overview
Handlers can now use screenshot-based detection to identify application states and perform UI automation. This approach is more reliable than window title matching and works across different application versions.

### Core Services for Screenshot Detection

#### 1. IScreenshotCaptureService
Captures screenshots of application windows:
```csharp
var screenshot = await _screenshotService.CaptureWindowAsync(windowHandle);
```

#### 2. ITemplateMatchingService
Finds UI elements within screenshots:
```csharp
var match = await _templateService.FindElementAsync(screenshot, "Windower/launch_arrow");
if (match.Confidence >= 0.80f)
{
    // Element found with high confidence
}
```

#### 3. IUIAutomationService
Performs clicks and keyboard input:
```csharp
var clickPoint = screenshot.ToScreenCoordinates(match.GetClickPoint());
await _automationService.ClickAsync(clickPoint);
```

#### 4. IScreenStateDetectionService
High-level state detection and waiting:
```csharp
var state = await _screenDetection.WaitForStateAsync(
    LoginTaskStep.ClickLaunchButton,
    windowHandle,
    TimeSpan.FromSeconds(10),
    (progress, msg) => subtask.UpdateProgress(progress, msg)
);
```

### Enhanced Handler Pattern with Screenshot Detection

```csharp
public class ScreenDetectionHandler : IAutoLoginStepHandler
{
    private readonly IScreenshotCaptureService _screenshotService;
    private readonly ITemplateMatchingService _templateService;
    private readonly IUIAutomationService _automationService;
    private readonly IScreenStateDetectionService _screenDetection;
    private readonly ILoggingService _loggingService;

    public LoginTaskStep HandledStep => LoginTaskStep.ClickLaunchButton;

    public async Task ExecuteAsync(
        AutoLoginSubtask subtask,
        AutoLoginQueueItem queueItem,
        CancellationToken cancellationToken)
    {
        subtask.Start();

        try
        {
            // Wait for the expected screen state
            subtask.UpdateProgress(20, "Waiting for application screen...");
            var screenState = await _screenDetection.WaitForStateAsync(
                HandledStep,
                queueItem.WindowHandle,
                TimeSpan.FromSeconds(subtask.EstimatedDurationSeconds * 2),
                (progress, message) => subtask.UpdateProgress(progress, message),
                cancellationToken);

            if (!screenState.IsValid || screenState.Confidence < 0.80f)
            {
                subtask.Fail($"Could not detect expected screen state. Confidence: {screenState.Confidence:P}");
                return;
            }

            // Execute the default action for this state
            subtask.UpdateProgress(60, "Performing action...");
            await _screenDetection.ExecuteDefaultActionAsync(
                queueItem.WindowHandle,
                screenState,
                cancellationToken);

            // Verify the action completed
            subtask.UpdateProgress(80, "Verifying action result...");
            await Task.Delay(500, cancellationToken);

            // Wait for transition to next state
            var transitioned = await _screenDetection.WaitForTransitionAsync(
                queueItem.WindowHandle,
                HandledStep,
                GetNextExpectedStep(),
                TimeSpan.FromSeconds(5),
                cancellationToken);

            if (!transitioned)
            {
                subtask.Fail("Action did not result in expected screen transition");
                return;
            }

            subtask.Complete();
        }
        catch (OperationCanceledException)
        {
            subtask.Cancel();
            throw;
        }
        catch (Exception ex)
        {
            subtask.Fail(ex.Message);
            await _loggingService.LogErrorAsync($"Screenshot detection handler failed", ex);
            throw;
        }
    }
}
```

### Window-Relative Coordinate System

All screenshot detection uses **window-relative coordinates**:

1. **Capture** - Screenshot of application window only
2. **Match** - Find UI elements within window bounds
3. **Convert** - Transform to screen coordinates for clicking

```csharp
// Example coordinate conversion
var windowScreenshot = await _screenshotService.CaptureWindowAsync(windowHandle);
var match = await _templateService.FindElementAsync(windowScreenshot, templatePath);

// match.GetClickPoint() returns window-relative coordinates
var windowRelativePoint = match.GetClickPoint();

// Convert to screen coordinates for clicking
var screenPoint = windowScreenshot.ToScreenCoordinates(windowRelativePoint);
await _automationService.ClickAsync(screenPoint);
```

### Template Management

Templates are embedded resources organized by application:

```
Templates/
├── Windower/
│   ├── launch_arrow.png
│   └── launch_arrow.json
├── PlayOnline/
│   ├── member_dropdown.png
│   └── member_dropdown.json
└── FFXI/
    ├── accept_button.png
    └── accept_button.json
```

### Creating Templates for Your Handler

1. **Capture UI Element**
   - Use screenshot tool to capture ONLY the UI element
   - Save as PNG in appropriate Templates folder
   - Keep small (50-200px typically)

2. **Create Metadata JSON**
   ```json
   {
     "name": "Element Name",
     "templatePath": "Application/element_name",
     "associatedStep": "LoginTaskStep",
     "action": {
       "type": "click",
       "clickOffset": { "x": 0, "y": 0 }
     },
     "confidenceThreshold": 0.80
   }
   ```

3. **Test Template Matching**
   ```csharp
   var template = await _templateManagement.LoadTemplateAsync("Application/element_name");
   var match = await _templateService.FindElementAsync(screenshot, template);
   Assert.IsTrue(match.Confidence >= 0.80f);
   ```

### Performance Considerations

1. **Cache Screenshots** - Don't recapture unnecessarily
2. **Use Regions** - Capture only relevant window areas
3. **Preload Templates** - Load at startup, not during execution
4. **Confidence Thresholds** - Balance accuracy vs. flexibility

### Debugging Screenshot Detection

```csharp
// Log match details for debugging
await _loggingService.LogDebugAsync(
    $"Template match: {match.Template.Name} at ({match.WindowRelativePosition.X}, {match.WindowRelativePosition.Y}) " +
    $"with confidence {match.Confidence:P}");

// Save screenshot for analysis (debug builds only)
#if DEBUG
if (match.Confidence < expectedConfidence)
{
    SaveScreenshotForDebug(windowScreenshot, $"low_confidence_{match.Template.Name}");
}
#endif
```

---

## 📚 Additional Resources

### Architecture Documentation
- `Services/AutoLoginQueueService.cs` - Main facade service
- `Models/AutoLoginTask.cs` - Task-level model
- `Models/AutoLoginSubtask.cs` - Subtask-level model
- `Models/LoginTaskStep.cs` - Available login steps

### Related Services
- `ILoggingService` - Comprehensive logging with Serilog
- `IProcessUtilityService` - Process management utilities
- `ISettingsService` - Application configuration
- `IQueueStateMachine` - State management for queue execution
- `IAutoLoginTaskExecutor` - Task/subtask execution coordinator

### Testing Framework
- MSTest for unit and integration tests
- Moq for service mocking
- Test project: `Testing/FFXIManager.Tests.csproj`

---

*This guide provides Claude Code with the patterns and templates needed to create consistent, maintainable auto-login handlers that integrate seamlessly with the refactored architecture.*