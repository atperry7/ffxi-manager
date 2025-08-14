# Settings Persistence Fix

## Issue Summary
The FFXIManager application experienced a settings persistence bug where configuration changes were lost between application sessions. Settings were being written to a temporary file (`FFXIManagerSettings.json.tmp`) but never properly renamed to the final settings file (`FFXIManagerSettings.json`), causing the application to revert to defaults on restart.

## Root Cause Analysis
The issue was in the `AtomicSave` method of the `SettingsService` class. The method used `File.Replace()` to atomically replace the settings file, but this operation has specific requirements:

1. **File.Replace() requires the destination file to exist** - On fresh installations, no `FFXIManagerSettings.json` file exists
2. **Silent failures** - The operation was failing silently due to generic exception handling
3. **File locking issues** - Concurrent access or antivirus software could prevent the operation

## Solution Architecture

### 1. Enhanced AtomicSave Method
- **Fallback Logic**: Detects if target file exists and uses `File.Move()` instead of `File.Replace()` when target doesn't exist
- **Retry Logic**: Implements exponential backoff retry mechanism for transient file system issues
- **Better Error Handling**: Proper cleanup of temporary files on failure

### 2. Automatic Initial File Creation
- **Proactive Approach**: Creates initial settings file on first application load
- **Prevents Future Issues**: Ensures target file always exists for subsequent atomic operations

### 3. Robust Retry Mechanism
The `RetryFileOperationAsync` method handles common file system issues:
- `IOException` - File locking, access conflicts
- `UnauthorizedAccessException` - Permission issues
- `DirectoryNotFoundException` - Missing directories (recreates them)
- **Exponential backoff**: 50ms → 100ms → 200ms → 400ms → 800ms → 1000ms (capped)

## Key Improvements

### Before
```csharp
// Old problematic code
File.Replace(tempPath, _settingsPath, null); // Fails if target doesn't exist
```

### After
```csharp
// New robust approach
if (File.Exists(_settingsPath))
{
    // Target exists, use atomic replace
    File.Replace(tempPath, _settingsPath, null);
}
else
{
    // Target doesn't exist, use move (effectively atomic on same volume)
    File.Move(tempPath, _settingsPath);
}
```

## Technical Benefits

1. **Maintains Atomicity**: Operations remain atomic whether using Replace or Move
2. **Handles Edge Cases**: Covers both existing and non-existing target files
3. **Resilient to Transient Issues**: Retry logic handles temporary file system problems
4. **Follows Existing Patterns**: Uses same retry pattern as `ProfileService`
5. **Clean Architecture**: No breaking changes to the public API

## Testing Verification

The fix was validated by:
1. Moving the orphaned `.tmp` file to prove recovery works
2. Clean compilation with no warnings
3. Preserving all existing functionality

## Prevention Measures

1. **Initial File Creation**: Settings file created on first load prevents the issue entirely
2. **Dual Approach**: Both proactive file creation AND fallback logic for maximum robustness  
3. **Proper Cleanup**: Temporary files cleaned up on failure to prevent accumulation

## Maintenance Notes

- The retry mechanism follows established patterns from `ProfileService.RetryIOAsync`
- Error handling intentionally avoids logging dependencies to prevent circular references
- The solution is self-contained within `SettingsService` with no external dependencies
- Future enhancements could include logging integration when available

## File Location
- **Fixed Class**: `Services/SettingsService.cs`
- **Key Methods**: `AtomicSave`, `RetryFileOperationAsync`, `LoadSettings`
- **No Interface Changes**: Maintains full backward compatibility
