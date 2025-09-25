# Auto-Login Handler Implementation Guide

## DirectX Application Support (Critical!)

**IMPORTANT**: PlayOnline and FFXI use DirectX rendering. Standard Win32 `BitBlt` captures return black screens. Our `ScreenshotCaptureService` now handles this automatically using `PrintWindow` API.

### Screenshot Capture Flow
```
1. Try PrintWindow() with PW_RENDERFULLCONTENT (for DirectX apps like PlayOnline/FFXI)
2. Fall back to BitBlt() for standard Windows applications
3. Diagnostic screenshots save automatically when diagnostics are enabled
```

## Quick Start: Creating a Handler

### 1. Handler Template
```csharp
public class YourStepHandler : BaseLoginTaskHandler
{
    private readonly IUIAutomationService _automationService;

    public YourStepHandler(
        ILoggingService loggingService,
        IScreenshotCaptureService screenshotService,
        ITemplateMatchingService templateService,
        IUIAutomationService automationService)
        : base(loggingService, screenshotService, templateService)
    {
        _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
    }

    public override LoginTaskStep TaskStep => LoginTaskStep.YourStep;

    public override bool CanHandle(AutoLoginSubtask subtask) => subtask.TaskStep == TaskStep;

    protected override async Task ExecuteHandlerLogicAsync(
        AutoLoginSubtask subtask,
        AutoLoginQueueItem queueItem,
        IAutoLoginContext context,
        CancellationToken cancellationToken)
    {
        subtask.Start();

        var accountName = queueItem.Account?.AccountName ?? "Unknown";
        await _loggingService.LogInfoAsync($"[FLOW] Starting {TaskStep} for {accountName}");

        // Get window handle
        var windowHandle = await FindWindowHandleAsync(
            subtask,
            "processName",
            "Application Name",
            cancellationToken);

        // Use standardized detection
        var screenMatch = await WaitForScreenDetectionAsync(
            subtask,
            "Templates/your_template",
            windowHandle,
            "screen description",
            cancellationToken,
            ScreenDetectionOptions.Extended); // Use Extended for complex screens

        if (screenMatch.Confidence < 0.80f)
        {
            throw new InvalidOperationException($"Could not detect screen (confidence: {screenMatch.Confidence:P})");
        }

        // Perform actions
        await ClickAtCoordinatesAsync(
            subtask,
            new Point(290, 390),
            windowHandle,
            "button description",
            cancellationToken,
            _automationService);

        subtask.Complete();
        await _loggingService.LogInfoAsync($"[FLOW] Completed {TaskStep} for {accountName}");
    }
}
```

## Proven Patterns from PlayOnline Implementation

### Window Finding (Standardized)
```csharp
// This pattern works for all applications
var windowHandle = await FindWindowHandleAsync(
    subtask,
    "pol",           // Process name
    "PlayOnline",    // Display name
    cancellationToken);
```

### Screen Detection (Extended Timeout for Complex Screens)
```csharp
// Use Extended timeout for loading screens, member selection, etc.
var memberScreenMatch = await WaitForScreenDetectionAsync(
    subtask,
    "PlayOnline/member_selection_screen",
    windowHandle,
    "member selection screen",
    cancellationToken,
    ScreenDetectionOptions.Extended); // 60-second timeout
```

### Coordinate Clicking (Proven Pattern)
```csharp
// Always use this exact pattern for clicking
await ClickAtCoordinatesAsync(
    subtask,
    new Point(290, 390),
    windowHandle,
    "CircleConfirmation button",
    cancellationToken,
    _automationService);
```

### Secure Text Entry
```csharp
// For password entry
await _automationService.TypeSecureTextAsync(password, 50, cancellationToken);
await Task.Delay(500, cancellationToken); // Allow typing to complete
```

### Multi-Step Flow Pattern
```csharp
protected override async Task ExecuteHandlerLogicAsync(...)
{
    subtask.Start();
    var accountName = queueItem.Account?.AccountName ?? "Unknown";

    // Phase 1: Find window
    subtask.UpdateProgress(5, "Finding active window...");
    var windowHandle = await FindWindowHandleAsync(/*...*/);

    // Phase 2: Wait for screen
    subtask.UpdateProgress(30, "Waiting for interface to load...");
    var screenMatch = await WaitForScreenDetectionAsync(/*...*/);

    // Phase 3: Validate detection
    if (screenMatch.Confidence < 0.80f)
    {
        throw new InvalidOperationException($"Detection failed (confidence: {screenMatch.Confidence:P})");
    }

    // Phase 4: Perform action
    subtask.UpdateProgress(70, "Performing action...");
    await ClickAtCoordinatesAsync(/*...*/);

    // Phase 5: Confirm completion
    subtask.UpdateProgress(95, "Confirming action...");
    await Task.Delay(1000, cancellationToken);

    subtask.Complete();
}
```

## Screen Detection Options (Proven Settings)

### Recommended Configurations
```csharp
public class ScreenDetectionOptions
{
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(1);
    public float ConfidenceThreshold { get; set; } = 0.80f;

    // Standard detection (30s timeout, 80% confidence)
    public static ScreenDetectionOptions Default => new();

    // For fast detections (UI elements that appear quickly)
    public static ScreenDetectionOptions Quick => new() { Timeout = TimeSpan.FromSeconds(10) };

    // For complex screens, loading screens, member selection (RECOMMENDED)
    public static ScreenDetectionOptions Extended => new() { Timeout = TimeSpan.FromSeconds(60) };

    // For critical elements that must be precise
    public static ScreenDetectionOptions HighConfidence => new() { ConfidenceThreshold = 0.90f };
}
```

## Template Management Best Practices

### Template File Organization
```
Templates/
├── PlayOnline/
│   ├── member_selection_screen.png
│   ├── virtual_keyboard_password_input_screen.png
│   ├── connect_to_playonline_screen.png
│   └── main_screen.png
├── FFXI/
│   ├── character_select_screen.png
│   └── world_select_screen.png
└── Windower/
    └── launch_arrow.png
```

### Critical Template Requirements
1. **Take templates from the SAME application** (PlayOnline templates from PlayOnline, not screenshots)
2. **Use diagnostic screenshots** to verify what the system actually sees
3. **Templates must be 24-bit RGB** (not RGBA) for consistent matching
4. **Crop tightly** to the essential UI elements
5. **Test confidence thresholds** - 80% works for most cases

## Diagnostic Features

### Screenshot Logging (Built-in)
```csharp
// Enable in Settings > Diagnostics > Enable Diagnostics
// Screenshots automatically saved to:
// %APPDATA%\FFXIManager\Diagnostics\Screenshots\
//
// Files are auto-cleaned:
// - Removes files older than 7 days
// - Keeps only latest 50 screenshots
```

### Progress Reporting Pattern
```csharp
subtask.UpdateProgress(5, "Finding window...");           // 5%
subtask.UpdateProgress(30, "Waiting for screen...");     // 30%
subtask.UpdateProgress(70, "Performing action...");      // 70%
subtask.UpdateProgress(95, "Confirming...");             // 95%
subtask.Complete();                                       // 100%
```

## Real-World Examples

### PlayOnline Member Selection (Working Implementation)
```csharp
// Phase 3: Wait for member selection screen with extended detection
subtask.UpdateProgress(30, "Waiting for member selection interface to load...");
var memberScreenMatch = await WaitForScreenDetectionAsync(
    subtask,
    "PlayOnline/member_selection_screen",
    windowHandle,
    "member selection screen",
    cancellationToken,
    ScreenDetectionOptions.Extended); // 60-second timeout for member selection

if (memberScreenMatch.Confidence < 0.80f)
{
    throw new InvalidOperationException($"Could not detect member selection screen after extended wait (confidence: {memberScreenMatch.Confidence:P})");
}
```

### Password Entry with Confirmation (Working Implementation)
```csharp
subtask.UpdateProgress(85, "Entering password securely...");
await _automationService.TypeSecureTextAsync(password, 50, cancellationToken);
await Task.Delay(500, cancellationToken);

// Click CircleConfirmation button to confirm password entry
subtask.UpdateProgress(95, "Confirming password entry...");
await ClickAtCoordinatesAsync(
    subtask,
    new Point(290, 390),
    windowHandle,
    "CircleConfirmation button",
    cancellationToken,
    _automationService);
await Task.Delay(1000, cancellationToken); // Allow confirmation to process

subtask.UpdateProgress(100, "Password entry and confirmation completed successfully");
```

## Critical Success Factors

### ✅ DO (Proven to Work)
- **Use `ScreenDetectionOptions.Extended`** for complex screens (60s timeout)
- **Always validate confidence >= 0.80f** before proceeding
- **Use diagnostic screenshots** to debug detection issues
- **Add confirmation clicks** after major actions (like password entry)
- **Handle DirectX applications** (automatic with our ScreenshotCaptureService)
- **Use window-relative coordinates** for clicking
- **Add delays after typing/clicking** for UI processing

### ❌ DON'T (Causes Failures)
- **Don't use Default timeouts** for complex screens (30s often insufficient)
- **Don't assume screenshots work** without testing DirectX capture
- **Don't skip confirmation steps** (like CircleConfirmation after password)
- **Don't use absolute screen coordinates** (use window-relative)
- **Don't ignore confidence scores** below 0.80f
- **Don't forget Task.Delay** after UI interactions

## Testing DirectX Applications

### Validation Checklist
1. **Enable Diagnostic Screenshots** in Settings
2. **Run auto-login process** and let it fail
3. **Check diagnostic screenshots** at `%APPDATA%\FFXIManager\Diagnostics\Screenshots\`
4. **Verify screenshots show actual content** (not black screens)
5. **If black screens**: DirectX capture issue (should be automatic now)
6. **If wrong content**: Window handle or timing issue
7. **If right content but 0% confidence**: Template mismatch issue

### Common DirectX Applications
- **PlayOnline Viewer** ✅ Supported (PrintWindow works)
- **Final Fantasy XI** ✅ Supported (PrintWindow works)
- **Windower** ✅ Supported (standard BitBlt works)
- **Other games** ✅ Should work (PrintWindow first, BitBlt fallback)

## Performance Considerations

### Timeout Strategy
```csharp
// Loading screens, complex interfaces
ScreenDetectionOptions.Extended;    // 60s timeout

// Simple UI elements, confirmations
ScreenDetectionOptions.Default;     // 30s timeout

// Quick validations, retries
ScreenDetectionOptions.Quick;       // 10s timeout
```

### Memory Management
- Screenshots are not cached (fresh capture each time)
- Templates are cached by TemplateManagementService
- Diagnostic screenshots auto-cleanup (50 files max, 7 days max)

---

## Registration Pattern
```csharp
// In Infrastructure/DependencyInjection.cs
services.AddSingleton<ILoginTaskHandler, YourStepHandler>();
```

**Remember: This guide is based on proven, working implementations. The DirectX screenshot capture and extended timeout patterns are critical for success with PlayOnline and FFXI applications.**