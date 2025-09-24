# Claude Code AutoLogin Handler Guide

## Quick Start Template

When asked to create an AutoLogin handler, use this template:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.Services.AutoLogin.ScreenDetection;

namespace FFXIManager.Services.AutoLogin.Handlers
{
    public class [StepName]Handler : IAutoLoginStepHandler
    {
        private readonly IScreenshotCaptureService _screenshotService;
        private readonly ITemplateMatchingService _templateService;
        private readonly IUIAutomationService _automationService;
        private readonly ILoggingService _loggingService;

        public [StepName]Handler(
            IScreenshotCaptureService screenshotService,
            ITemplateMatchingService templateService,
            IUIAutomationService automationService,
            ILoggingService loggingService)
        {
            _screenshotService = screenshotService ?? throw new ArgumentNullException(nameof(screenshotService));
            _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
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
                // 1. Capture window screenshot
                subtask.UpdateProgress(20, "Capturing application window...");
                var screenshot = await _screenshotService.CaptureWindowAsync(queueItem.WindowHandle, cancellationToken);

                if (screenshot == null || !screenshot.IsValid)
                {
                    subtask.Fail("Failed to capture window screenshot");
                    return;
                }

                // 2. Find UI element
                subtask.UpdateProgress(40, "Locating UI element...");
                var match = await _templateService.FindElementAsync(
                    screenshot,
                    "[Application]/[element_name]",
                    cancellationToken);

                if (match.Confidence < 0.80f)
                {
                    subtask.Fail($"Could not find UI element (confidence: {match.Confidence:P})");
                    return;
                }

                // 3. Perform action
                subtask.UpdateProgress(60, "Performing action...");
                var clickPoint = screenshot.ToScreenCoordinates(match.GetClickPoint());
                await _automationService.ClickAsync(clickPoint, cancellationToken);

                // 4. Wait for result
                subtask.UpdateProgress(80, "Verifying action...");
                await Task.Delay(500, cancellationToken);

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
                await _loggingService.LogErrorAsync($"Handler {HandledStep} failed", ex);
                throw;
            }
        }
    }
}
```

## Step-by-Step Implementation

### 1. Check LoginTaskStep enum
Look at `Models/LoginTaskStep.cs` for available steps:
- LaunchWindower
- MemberSelection
- PasswordEntry
- CharacterSelection
- etc.

### 2. Create Handler File
Location: `Services/AutoLogin/Handlers/[StepName]Handler.cs`

### 3. Create Template Files
```
Templates/
├── Windower/
│   └── element_name.json
├── PlayOnline/
│   └── element_name.json
└── FFXI/
    └── element_name.json
```

Template JSON structure:
```json
{
  "name": "Element Name",
  "templatePath": "Application/element_name",
  "associatedStep": "LoginTaskStep",
  "action": {
    "type": "click",
    "clickOffset": { "x": 30, "y": 15 }
  },
  "confidenceThreshold": 0.80
}
```

## Template Creation Strategy

### Full-Context Screenshots (Recommended)

**Use full application window screenshots** rather than small element crops for better accuracy and robustness.

**Benefits:**
- **Uniqueness**: Prevents matching similar elements in different contexts
- **State Validation**: Ensures application is in the expected state before clicking
- **Future-Proofing**: Handles complex UIs where similar elements exist
- **Robustness**: Less sensitive to minor UI variations

**Template Creation Process:**
1. Take screenshot of the **entire application window** in the desired state
2. Save as `Templates/[App]/[element_name].png`
3. Identify the pixel coordinates of the clickable element within this image
4. Set `clickOffset` to these absolute coordinates (relative to template top-left)

**Coordinate System:**
- `clickOffset` coordinates are **relative to template image top-left corner**
- Template matching finds the window → adds offset to get click position
- Example: If arrow button is at pixel (544, 802) in template image, use:
  ```json
  "clickOffset": { "x": 544, "y": 802 }
  ```

**Example - Windower Launch Arrow:**
```json
{
  "name": "Windower Launch Arrow",
  "templatePath": "Windower/launch_arrow",
  "associatedStep": "ClickLaunchButton",
  "action": {
    "type": "click",
    "clickOffset": { "x": 544, "y": 802 }  // Absolute position within template
  },
  "confidenceThreshold": 0.80
}
```

### Small Element Crops (Alternative)

Only use when full-context approach isn't suitable:
- Template contains just the UI element to click
- Use `clickOffset`: { "x": 0, "y": 0 } to click center
- Or small relative offsets like { "x": 2, "y": -1 }

### 4. Register in DI Container
Add to `Infrastructure/DependencyInjection.cs`:
```csharp
services.AddTransient<IAutoLoginStepHandler, [StepName]Handler>();
```

### 5. Create Test File
Location: `Testing/Services/AutoLogin/Handlers/[StepName]HandlerTests.cs`

## Context Sharing Between Steps

**CRITICAL**: Multi-step handlers must share context data between steps to pass information like ProcessId and WindowHandle.

### Pattern: Persistent Context Storage
```csharp
public class MultiStepHandler : ILoginTaskHandler
{
    private readonly Dictionary<string, object> _contextStorage = new();

    public async Task ExecuteAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, CancellationToken cancellationToken)
    {
        // Create or get execution context for shared data - use persistent context per queue item
        var contextKey = $"ExecutionContext_{queueItem.Id}";
        if (!_contextStorage.TryGetValue(contextKey, out var existingContext) || existingContext is not TaskExecutionContext context)
        {
            context = new TaskExecutionContext
            {
                QueueItem = queueItem,
                Subtask = subtask,
                CancellationToken = cancellationToken
            };
            _contextStorage[contextKey] = context;
            await _loggingService.LogInfoAsync($"Created new execution context for queue item {queueItem.Id}");
        }
        else
        {
            // Update the subtask and cancellation token for the current step
            context.Subtask = subtask;
            context.CancellationToken = cancellationToken;
            await _loggingService.LogInfoAsync($"Reusing existing execution context for queue item {queueItem.Id} (SharedData has {context.SharedData.Count} items)");
        }

        // Use context throughout the method...
    }
}
```

### Sharing Data Between Steps
```csharp
// Step 1: Store data
context.SharedData["ProcessId"] = processId;
context.SharedData["WindowHandle"] = windowHandle;

// Step 2: Retrieve data
if (!context.SharedData.TryGetValue("ProcessId", out var processIdObj) || processIdObj is not int processId)
{
    subtask.Fail("No process ID available from previous step");
    return;
}

if (!context.SharedData.TryGetValue("WindowHandle", out var windowHandleObj) || windowHandleObj is not IntPtr windowHandle)
{
    subtask.Fail("No window handle available from previous step");
    return;
}
```

**Why This Matters**: Without context sharing, each step gets a fresh empty context, causing steps to fail silently when they can't find expected data from previous steps.

## Common Patterns

### Text Input Pattern
```csharp
// Click field first
var fieldMatch = await _templateService.FindElementAsync(screenshot, "PlayOnline/password_field");
var fieldClick = screenshot.ToScreenCoordinates(fieldMatch.GetClickPoint());
await _automationService.ClickAsync(fieldClick, cancellationToken);

// Clear and type
await _automationService.ClearFieldAsync(cancellationToken);
await _automationService.TypeSecureTextAsync(queueItem.Account.Password, 50, cancellationToken);
```

### Wait for Screen Transition
```csharp
// After action, wait for next screen
for (int i = 0; i < 10; i++)
{
    await Task.Delay(500, cancellationToken);
    var newScreenshot = await _screenshotService.CaptureWindowAsync(queueItem.WindowHandle);
    var nextElement = await _templateService.FindElementAsync(newScreenshot, "NextScreen/element");

    if (nextElement.Confidence >= 0.80f)
    {
        break; // Transition complete
    }
}
```

### Multiple UI Elements
```csharp
// Check for multiple possible states
var templates = new[] { "button_normal", "button_hover", "button_disabled" };
TemplateMatchResult? bestMatch = null;

foreach (var template in templates)
{
    var match = await _templateService.FindElementAsync(screenshot, $"Application/{template}");
    if (match.Confidence > (bestMatch?.Confidence ?? 0))
    {
        bestMatch = match;
    }
}
```

## Key Rules

### ✅ ALWAYS DO
1. Call `subtask.Start()` first
2. Update progress frequently (every 20-25%)
3. Check confidence thresholds (≥ 0.80)
4. Handle cancellation properly
5. Call `subtask.Complete()` or `subtask.Fail()`
6. Use window-relative coordinates
7. Add null checks for screenshots
8. **Use persistent context storage for multi-step handlers**
9. **Validate context data at the start of each step**
10. **Add logging for context creation/reuse for debugging**

### ❌ NEVER DO
1. Don't manually set `subtask.Status`
2. Don't use full-screen screenshots
3. Don't hardcode screen coordinates
4. Don't log sensitive data (passwords)
5. Don't skip error handling
6. Don't forget cancellationToken
7. **Don't create new context for each step** - use persistent storage
8. **Don't ignore context data validation** - always check for required data

## Troubleshooting Common Issues

### Silent Step Failures
**Problem**: Handler steps complete in ~1 second without executing logic or producing expected results.

**Cause**: Missing context data from previous steps causing early returns.

**Solution**:
1. Implement persistent context storage (see Context Sharing section above)
2. Add validation logging for context data
3. Check that `SharedData` contains expected keys from previous steps

### Debug Logging Pattern
```csharp
// Add flow logging to track execution
await _loggingService.LogInfoAsync($"[FLOW] Starting {subtask.TaskStep} for {queueItem.DisplayName}");
await _loggingService.LogInfoAsync($"[FLOW] Context has {context.SharedData.Count} shared items");

// Validate required context data
if (!context.SharedData.TryGetValue("RequiredKey", out var data))
{
    await _loggingService.LogInfoAsync($"[FLOW] Missing required context data: RequiredKey");
    subtask.Fail("Required data not available from previous step");
    return;
}
```

## Quick Reference

### Available Services
- `IScreenshotCaptureService` - Window capture
- `ITemplateMatchingService` - Find UI elements
- `IUIAutomationService` - Click/type actions
- `ILoggingService` - Logging

### AutoLoginSubtask Methods
- `Start()` - Begin execution
- `UpdateProgress(int, string)` - Update UI
- `Complete()` - Mark successful
- `Fail(string)` - Mark failed
- `Skip(string)` - Mark skipped
- `Cancel()` - Mark cancelled

### Coordinate Conversion
```csharp
// Window-relative → Screen
var screenPt = screenshot.ToScreenCoordinates(windowRelativePt);

// Click at element center
var clickPt = screenshot.ToScreenCoordinates(match.GetClickPoint());
```

## File Locations
- **Handler**: `Services/AutoLogin/Handlers/[Name]Handler.cs`
- **Test**: `Testing/Services/AutoLogin/Handlers/[Name]HandlerTests.cs`
- **Template**: `Templates/[App]/[element].json`
- **Register**: `Infrastructure/DependencyInjection.cs`

## Build & Test Commands
```bash
dotnet build FFXIManager.csproj
dotnet test Testing/FFXIManager.Tests.csproj
```

---
*Use this guide to quickly create consistent, working AutoLogin handlers with screenshot detection.*