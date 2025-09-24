# AutoLogin Context Management Architecture

## Problem Statement

The current context management approach has several architectural flaws:

1. **Fragmented Context Storage**: Each handler maintains its own context dictionary
2. **Memory Leaks**: Contexts are never cleaned up
3. **No Thread Safety**: Concurrent access issues possible
4. **Silent Dependencies**: Steps implicitly depend on previous steps' data
5. **No Lifecycle Management**: No cleanup when queue items complete/fail

## Proposed Solution: Centralized Context Service

### 1. IAutoLoginContextService Interface

```csharp
public interface IAutoLoginContextService
{
    /// <summary>
    /// Gets or creates context for a queue item
    /// </summary>
    IAutoLoginContext GetContext(string queueItemId);

    /// <summary>
    /// Validates that required context data is available
    /// </summary>
    Task<bool> ValidateContextAsync(string queueItemId, string[] requiredKeys);

    /// <summary>
    /// Cleans up context when queue item completes
    /// </summary>
    Task DisposeContextAsync(string queueItemId);

    /// <summary>
    /// Cleans up all expired contexts
    /// </summary>
    Task CleanupExpiredContextsAsync();
}
```

### 2. AutoLoginContext Implementation

```csharp
public interface IAutoLoginContext : IDisposable
{
    string QueueItemId { get; }
    DateTime CreatedAt { get; }
    DateTime LastAccessed { get; }

    /// <summary>
    /// Thread-safe context data storage
    /// </summary>
    ConcurrentDictionary<string, object> Data { get; }

    /// <summary>
    /// Store typed data with validation
    /// </summary>
    void SetData<T>(string key, T value);

    /// <summary>
    /// Retrieve typed data with validation
    /// </summary>
    T? GetData<T>(string key) where T : class;

    /// <summary>
    /// Check if required data exists
    /// </summary>
    bool HasData(params string[] keys);
}
```

### 3. Handler Context Requirements

```csharp
/// <summary>
/// Attribute to declare required context data
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class RequiresContextDataAttribute : Attribute
{
    public string[] RequiredKeys { get; }

    public RequiresContextDataAttribute(params string[] requiredKeys)
    {
        RequiredKeys = requiredKeys;
    }
}

// Usage in handlers:
[RequiresContextData("ProcessId", "WindowHandle")]
public class ClickLaunchButtonHandler : ILoginTaskHandler
{
    // Handler implementation...
}
```

### 4. Enhanced TaskExecutor with Context Management

```csharp
public class AutoLoginTaskExecutor
{
    private readonly IAutoLoginContextService _contextService;

    public async Task ExecuteHandlerAsync(ILoginTaskHandler handler, AutoLoginSubtask subtask, AutoLoginQueueItem queueItem)
    {
        // Get context for this queue item
        var context = _contextService.GetContext(queueItem.Id);

        // Validate required context data if handler declares it
        var requiredDataAttr = handler.GetType().GetCustomAttribute<RequiresContextDataAttribute>();
        if (requiredDataAttr != null)
        {
            if (!await _contextService.ValidateContextAsync(queueItem.Id, requiredDataAttr.RequiredKeys))
            {
                subtask.Fail($"Missing required context data: {string.Join(", ", requiredDataAttr.RequiredKeys)}");
                return;
            }
        }

        // Execute handler with context
        await handler.ExecuteAsync(subtask, queueItem, context, cancellationToken);
    }

    private async Task CleanupQueueItemContext(string queueItemId)
    {
        await _contextService.DisposeContextAsync(queueItemId);
    }
}
```

## Implementation Strategy

### Phase 1: Core Infrastructure
1. Create `IAutoLoginContextService` and implementation
2. Create `IAutoLoginContext` with thread-safe storage
3. Register in DI container
4. Add context cleanup to queue completion events

### Phase 2: Handler Migration
1. Update `ILoginTaskHandler` interface to accept `IAutoLoginContext`
2. Create base handler class with context helpers
3. Migrate existing handlers one by one
4. Remove handler-specific context storage

### Phase 3: Enhanced Features
1. Add `RequiresContextDataAttribute` for validation
2. Implement automatic context validation
3. Add context monitoring/diagnostics
4. Add context persistence for retry scenarios

## Benefits

### 1. **Centralized Management**
- Single source of truth for context data
- Consistent lifecycle management
- Unified cleanup and monitoring

### 2. **Better Error Handling**
- Explicit dependency declaration
- Clear error messages for missing data
- Early validation before handler execution

### 3. **Memory Management**
- Automatic cleanup when queue items complete
- Configurable expiration for abandoned contexts
- Monitoring for context usage patterns

### 4. **Thread Safety**
- ConcurrentDictionary for safe concurrent access
- Atomic operations for context lifecycle
- No race conditions between handlers

### 5. **Debugging & Monitoring**
- Context access logging
- Context lifecycle tracking
- Clear visibility into data flow between steps

## Migration Path

### Current State
```csharp
// In handler
private readonly Dictionary<string, object> _contextStorage = new();
```

### Target State
```csharp
// Injected service
private readonly IAutoLoginContextService _contextService;

// In ExecuteAsync
var context = _contextService.GetContext(queueItem.Id);
context.SetData("ProcessId", processId);
var windowHandle = context.GetData<IntPtr>("WindowHandle");
```

## Configuration

```json
{
  "AutoLogin": {
    "Context": {
      "ExpirationMinutes": 30,
      "CleanupIntervalMinutes": 5,
      "MaxContextsPerUser": 10
    }
  }
}
```

## Considerations

1. **Backward Compatibility**: Gradual migration approach maintains existing functionality
2. **Performance**: ConcurrentDictionary provides good performance for concurrent access
3. **Memory Usage**: Automatic cleanup prevents memory leaks
4. **Extensibility**: Interface-based design allows for different context storage backends
5. **Testing**: Mockable interfaces enable comprehensive unit testing

---

This architecture provides a robust, scalable foundation for context management in the AutoLogin system while addressing all current architectural limitations.