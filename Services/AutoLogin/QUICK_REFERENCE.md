# Handler Implementation Quick Reference

## 🚀 Getting Started Checklist

```csharp
// 1. Extend BaseLoginTaskHandler (recommended)
public class MyHandler : BaseLoginTaskHandler
{
    public MyHandler(ILoggingService loggingService) : base(loggingService) { }

    public override LoginTaskStep TaskStep => LoginTaskStep.YourStep;

    public override bool CanHandle(AutoLoginSubtask subtask) =>
        subtask.TaskStep == LoginTaskStep.YourStep;

    protected override async Task ExecuteHandlerLogicAsync(
        AutoLoginSubtask subtask,
        AutoLoginQueueItem queueItem,
        CancellationToken cancellationToken)
    {
        // Your implementation here
    }
}
```

## 🎯 Essential Patterns (Copy-Paste Ready)

### Progress Reporting
```csharp
await UpdateProgressAsync(subtask, 25, "Descriptive message...");
```

### Cancellation Checking
```csharp
cancellationToken.ThrowIfCancellationRequested();
await Task.Delay(1000, cancellationToken); // Always pass token
```

### Retry with Backoff
```csharp
var result = await ExecuteWithRetryAsync(
    async (ct) => await SomeOperation(ct),
    "operation description",
    maxRetries: 3,
    cancellationToken: cancellationToken
);
```

### Wait for Condition
```csharp
await WaitForConditionAsync(
    async () => await CheckSomething(),
    "waiting for something",
    subtask,
    queueItem.Task,
    TimeSpan.FromSeconds(30),
    progressStart: 20,
    progressEnd: 80,
    cancellationToken: cancellationToken
);
```

### Input Validation
```csharp
ValidateAccountProperty(queueItem.Account.AccountName, "Account Name", "this operation");
```

### Secure Logging
```csharp
await LogSecureOperationAsync("password entry", queueItem.Account.AccountName);
```

## 🔄 State Machine Respect

```csharp
// Always check pause state in loops
while (task.Status == AutoLoginTaskStatus.Paused && !cancellationToken.IsCancellationRequested)
{
    await Task.Delay(100, cancellationToken);
}
cancellationToken.ThrowIfCancellationRequested();
```

## 🚨 Common Gotchas

### ❌ Don't Do This
```csharp
// Missing cancellation token
await Task.Delay(1000);

// Not checking pause state
while (true) { /* long operation */ }

// Logging sensitive data
_logger.LogInfo($"Password: {password}");

// Not validating inputs
var name = queueItem.Account.SomeProperty; // Might be null!
```

### ✅ Do This Instead
```csharp
// Include cancellation token
await Task.Delay(1000, cancellationToken);

// Check pause state
while (condition && !cancellationToken.IsCancellationRequested)
{
    // Check pause
    while (task.Status == AutoLoginTaskStatus.Paused && !cancellationToken.IsCancellationRequested)
    {
        await Task.Delay(100, cancellationToken);
    }
    cancellationToken.ThrowIfCancellationRequested();

    // Do work...
}

// Mask sensitive data
await LogSecureOperationAsync("credential operation", accountName);

// Validate first
ValidateAccountProperty(queueItem.Account.SomeProperty, "SomeProperty", "operation");
```

## 🧪 Testing Template

```csharp
[TestMethod]
public async Task Execute_ValidInputs_CompletesSuccessfully()
{
    // Arrange
    var mockLogging = new Mock<ILoggingService>();
    var handler = new MyHandler(mockLogging.Object);
    var subtask = new AutoLoginSubtask { TaskStep = LoginTaskStep.MyStep };
    var queueItem = CreateValidQueueItem();

    // Act
    await handler.ExecuteAsync(subtask, queueItem, CancellationToken.None);

    // Assert
    Assert.AreEqual(AutoLoginSubtaskStatus.Completed, subtask.Status);
    Assert.AreEqual(100, subtask.Progress);
}
```

## 🏗️ UI Automation Snippets

### Window Finding
```csharp
[DllImport("user32.dll")]
private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

var windowHandle = FindWindow(null, "Window Title");
if (windowHandle == IntPtr.Zero)
    throw new InvalidOperationException("Window not found");
```

### UI Automation (requires System.Windows.Automation)
```csharp
var automation = AutomationElement.FromHandle(windowHandle);
var button = automation.FindFirst(TreeScope.Descendants,
    new PropertyCondition(AutomationElement.NameProperty, "Button Text"));

if (button != null && button.Current.IsEnabled)
{
    var clickablePoint = button.GetClickablePoint();
    // Perform click
}
```

## 📋 Available Account Properties

```csharp
// These are the actual properties available:
queueItem.Account.AccountName         // string
queueItem.Account.POLMemberSlot       // int (1-4)
queueItem.Account.FFXICharacterSlot   // int (1-16)
queueItem.Account.HasStoredPassword   // bool
queueItem.Account.IsOTPEnabled        // bool
queueItem.Account.OTPConfiguration    // OTPConfiguration?
```

## 🎛️ Handler Registration

Add to `Infrastructure/DependencyInjection.cs`:
```csharp
services.AddSingleton<ILoginTaskHandler, MyNewHandler>();
```

## 📝 Documentation Template

```csharp
/// <summary>
/// Handles [describe what this handler does].
/// Responsible for: [list the LoginTaskSteps this handles]
/// </summary>
public class MyHandler : BaseLoginTaskHandler
{
    // Implementation...
}
```

## 🔧 Configuration Pattern

```csharp
public class MyHandlerSettings
{
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetries { get; set; } = 3;
    public string SomeRequiredPath { get; set; } = "";
}

// In handler constructor:
private readonly MyHandlerSettings _settings;
// Get from ISettingsService or configuration
```

## 🚀 Implementation Order

1. **Start with simulation** - Get the flow working
2. **Add real window detection** - Find the target application
3. **Implement UI element finding** - Locate buttons, fields, etc.
4. **Add interaction logic** - Click, type, wait
5. **Enhance error handling** - Handle edge cases
6. **Add configuration** - Make paths/settings configurable
7. **Write tests** - Unit and integration tests
8. **Performance tune** - Optimize timing and reliability

---

*Keep this reference handy while implementing! For full details, see HANDLER_IMPLEMENTATION_GUIDE.md*