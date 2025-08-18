# Architecture Cleanup Plan

## Current Issues

### 1. Duplicate Window Activation Code
- **ProcessManagementService.ActivateWindowAsync()** - 100+ lines
- **ProcessUtilityService.ActivateWindowAsync()** - 100+ lines  
- **ProcessUtilityService.ActivateWindowEnhancedAsync()** - NEW enhanced version

**Impact**: 300+ lines of duplicate code, maintenance nightmare, inconsistent behavior

### 2. Service Responsibility Confusion

#### ProcessManagementService
- ✅ Process monitoring
- ✅ Process lifecycle management
- ❌ Window activation (should delegate)
- ❌ Window enumeration (duplicate)

#### ProcessUtilityService  
- ✅ Process utilities (kill, query)
- ❌ Window activation (duplicate)
- ✅ Enhanced activation (new)

#### UnifiedMonitoringService
- ✅ Multi-profile monitoring
- ❌ Overlaps with ProcessManagementService
- ❓ Unclear separation of concerns

#### PlayOnlineMonitorService
- ✅ FFXI-specific logic
- ❌ Calls ProcessManagementService.ActivateWindowAsync
- ❌ Should use enhanced activation

### 3. Inconsistent Error Handling
- Old activation methods return bool
- New enhanced method returns detailed WindowActivationResult
- No consistent failure reporting

## Recommended Cleanup

### Phase 1: Consolidate Window Activation

1. **Remove duplicate ActivateWindowAsync from ProcessManagementService**
   - Delete lines 388-500+ 
   - Update to use ProcessUtilityService

2. **Make ProcessUtilityService the single source of truth for window operations**
   - Keep ActivateWindowEnhancedAsync as primary
   - Update ActivateWindowAsync to call enhanced version internally
   - All window operations go through ProcessUtilityService

3. **Update PlayOnlineMonitorService**
   ```csharp
   // OLD
   var success = await processManagement.ActivateWindowAsync(character.WindowHandle);
   
   // NEW  
   var result = await processUtility.ActivateWindowEnhancedAsync(character.WindowHandle);
   if (!result.Success) {
       // Log specific failure reason
       await LogActivationFailure(result);
   }
   ```

### Phase 2: Clarify Service Responsibilities

#### ProcessManagementService → ProcessMonitorService
- **Rename** to clarify it's for monitoring
- **Responsibilities**:
  - Process lifecycle monitoring
  - Process events (started/stopped)
  - Process tracking
- **Remove**:
  - Window activation code
  - Duplicate window enumeration

#### ProcessUtilityService → WindowManagementService
- **Rename** to clarify window focus
- **Responsibilities**:
  - ALL window operations
  - Window activation (enhanced)
  - Window state queries
  - Window enumeration
- **Add**:
  - Consolidated window utilities

#### UnifiedMonitoringService
- **Evaluate**: Is this needed or can ProcessMonitorService handle it?
- **If kept**: Clear separation - this is for multi-app monitoring
- **If removed**: Merge useful parts into ProcessMonitorService

### Phase 3: Update Dependency Flow

```
PlayOnlineMonitorService
    ↓
WindowManagementService (formerly ProcessUtilityService)
    ↓
Enhanced Window Activation
```

### Phase 4: Standardize Return Types

All activation methods should return `WindowActivationResult`:
- Rich failure information
- Performance metrics
- Diagnostic details

## Benefits

1. **-300 lines** of duplicate code
2. **Single source of truth** for window operations
3. **Clear service responsibilities**
4. **Better error diagnostics**
5. **Easier maintenance**
6. **Consistent behavior**

## Implementation Order

1. ✅ Create enhanced activation (DONE)
2. Update PlayOnlineMonitorService to use enhanced
3. Remove ProcessManagementService.ActivateWindowAsync
4. Rename services for clarity
5. Consolidate monitoring services
6. Update all callers
7. Remove dead code

## Dead Code to Remove

- ProcessManagementService.ActivateWindowAsync (lines 388-500+)
- Duplicate P/Invoke declarations
- Unused monitoring profiles in UnifiedMonitoringService
- Legacy error handling paths

## Testing Impact

- Update unit tests to use new WindowActivationResult
- Remove tests for deleted methods
- Add tests for specific failure scenarios