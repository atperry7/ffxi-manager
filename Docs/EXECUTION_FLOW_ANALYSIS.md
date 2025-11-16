# Auto-Login Execution Flow Analysis

This document traces the complete execution flow from queue creation to task execution, with special focus on **SkipIfApplicationRunning** logic placement.

---

## Executive Summary

**Skip Logic Location:** ✅ **Task Building Phase** (WorkflowTaskBuilder)
**Evaluation Timing:** Once, when character is added to queue
**Handler Behavior:** 100% data-driven, no hardcoded skip logic

---

## Phase 1: Queue Creation & Task Building

### Entry Point: User Adds Character to Queue

```
User clicks "Add to Queue"
  ↓
AutoLoginQueueService.EnqueueCharacterAsync()
  ↓
WorkflowTaskBuilder.BuildSubtasksAsync(workflow, account)
```

### WorkflowTaskBuilder.BuildSubtasksAsync()

**Location:** `Services/AutoLogin/WorkflowTaskBuilder.cs:46-88`

```csharp
public async Task<List<AutoLoginSubtask>> BuildSubtasksAsync(
    WorkflowDefinition workflow,
    PlayOnlineMemberAccount account,
    CancellationToken cancellationToken = default)
{
    // 1. Validate workflow
    if (!workflow.Validate(out var errors))
        throw new InvalidOperationException(...);

    // 2. Create condition evaluator with application state snapshot
    var conditionEvaluator = await CreateConditionEvaluatorAsync(account);

    // 3. Filter steps based on IsEnabled, Condition, and SkipIfApplicationRunning
    var executableSteps = workflow.GetExecutableSteps(conditionEvaluator);

    // 4. Convert filtered steps to subtasks
    var subtasks = new List<AutoLoginSubtask>();
    foreach (var step in executableSteps)
    {
        var subtask = CreateSubtaskFromStep(step, executionOrder++);
        subtasks.Add(subtask);
    }

    return subtasks;
}
```

**Key Point:** This is where **ALL filtering happens**, including skip logic.

---

### CreateConditionEvaluatorAsync() - The Skip Logic Core

**Location:** `Services/AutoLogin/WorkflowTaskBuilder.cs:124-162`

```csharp
private async Task<Func<WorkflowStepDefinition, bool>> CreateConditionEvaluatorAsync(
    PlayOnlineMemberAccount account)
{
    // 1. Load current application states (SNAPSHOT at queue creation time)
    var applications = await _externalApplicationService.GetApplicationsAsync();
    var runningAppNames = applications
        .Where(app => app.IsRunning)
        .Select(app => app.Name)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    await _loggingService.LogDebugAsync($"Running applications: {string.Join(", ", runningAppNames)}");

    // 2. Return evaluator function
    return step =>
    {
        // CHECK 1: SkipIfApplicationRunning (FIRST CHECK - highest priority)
        if (!string.IsNullOrWhiteSpace(step.SkipIfApplicationRunning))
        {
            if (runningAppNames.Contains(step.SkipIfApplicationRunning))
            {
                _loggingService.LogInfoAsync(
                    $"Skipping step '{step.DisplayName}' because '{step.SkipIfApplicationRunning}' is running"
                ).Wait();
                return false; // ❌ Skip this step
            }
        }

        // CHECK 2: Legacy Condition property (for account properties only)
        if (string.IsNullOrWhiteSpace(step.Condition))
            return true; // ✅ No condition means execute

        try
        {
            return EvaluateCondition(step.Condition, account);
        }
        catch (Exception ex)
        {
            _loggingService.LogWarningAsync($"Failed to evaluate condition: {ex.Message}").Wait();
            return true; // ✅ Default to executing on error
        }
    };
}
```

**Critical Behavior:**
- ✅ `SkipIfApplicationRunning` checked **FIRST** (lines 138-145)
- ✅ Application state captured as **snapshot** at queue creation time
- ✅ Condition evaluator returns `false` = skip step, `true` = execute step
- ✅ No hardcoded application names - completely data-driven

---

### WorkflowDefinition.GetExecutableSteps()

**Location:** `Models/AutoLogin/WorkflowDefinition.cs:231-238`

```csharp
public List<WorkflowStepDefinition> GetExecutableSteps(
    Func<WorkflowStepDefinition, bool>? conditionEvaluator = null)
{
    return Steps
        .Where(s => s.IsEnabled)                            // Filter 1: Disabled steps
        .Where(s => conditionEvaluator?.Invoke(s) ?? true)  // Filter 2: Skip conditions
        .OrderBy(s => s.Order)                              // Sort by order
        .ToList();
}
```

**Multi-Stage Filtering:**
1. **IsEnabled = false** → Step removed (user disabled in UI)
2. **SkipIfApplicationRunning matches** → Step removed (app running)
3. **Condition = false** → Step removed (account property mismatch)
4. **Result:** Only executable steps become subtasks

---

## Phase 2: Queue Execution

### Entry Point: Queue Starts

```
User clicks "Start Queue"
  ↓
AutoLoginQueueService.StartQueueAsync()
  ↓
QueueExecutionOrchestrator.ProcessQueueAsync()
  ↓
AutoLoginTaskExecutor.ExecuteAsync(queueItem)
```

### AutoLoginTaskExecutor - Per Character

**Location:** `Services/AutoLogin/AutoLoginTaskExecutor.cs`

```csharp
public async Task ExecuteAsync(AutoLoginQueueItem queueItem, ...)
{
    // 1. Get subtasks built during Phase 1
    var subtasks = queueItem.Task.Subtasks;

    // 2. Execute each subtask in order
    foreach (var subtask in subtasks)
    {
        // 3. Resolve handler for subtask
        var handler = _handlerResolver.ResolveHandler(subtask);

        // 4. Execute via handler
        var result = await handler.ExecuteAsync(subtask, queueItem, context, ct);

        if (!result.Success)
            break; // Abort on failure
    }
}
```

**Key Point:** Handler only sees **filtered subtasks** from Phase 1. Skipped steps never reach execution.

---

## Phase 3: DynamicWorkflowHandler Execution

### DynamicWorkflowHandler.ExecuteHandlerLogicAsync()

**Location:** `Services/AutoLogin/DynamicWorkflowHandler.cs:66-80`

```csharp
protected override async Task ExecuteHandlerLogicAsync(
    AutoLoginSubtask subtask,
    AutoLoginQueueItem queueItem,
    IAutoLoginContext context,
    CancellationToken cancellationToken)
{
    var stepDef = subtask.WorkflowStep!;

    await _loggingService.LogInfoAsync(
        $"[DYNAMIC-WORKFLOW] Executing step: {stepDef.DisplayName} (StepId: {stepDef.StepId})"
    );

    // Execute navigation step (unified flow)
    await ExecuteNavigationStepAsync(subtask, queueItem, context, cancellationToken);

    await _loggingService.LogInfoAsync(
        $"[DYNAMIC-WORKFLOW] Completed step: {stepDef.DisplayName}"
    );
}
```

**✅ NO Hardcoded Skip Logic:**
- No checks for specific application names
- No conditional branching based on external state
- No filtering or skipping - just executes the step it receives
- 100% data-driven from WorkflowStepDefinition

---

### ExecuteNavigationStepAsync() - The Execution Flow

**Location:** `Services/AutoLogin/DynamicWorkflowHandler.cs:86-125`

```csharp
private async Task ExecuteNavigationStepAsync(...)
{
    var stepDef = subtask.WorkflowStep!;

    // Phase 1: Validate step definition
    ValidateWorkflowStep(stepDef);

    // Phase 2: Conditionally get window handle (skip for Launch/Wait-only steps)
    var requiresWindow = StepRequiresWindow(stepDef);
    var windowHandle = requiresWindow
        ? await GetOrDiscoverWindowHandleAsync(...)
        : IntPtr.Zero;

    // Phase 3: Detect screen (if TemplatePath configured)
    TemplateMatchResult? templateMatch = null;
    if (!string.IsNullOrWhiteSpace(stepDef.TemplatePath))
    {
        templateMatch = await DetectScreenAsync(stepDef, windowHandle, ...);
    }

    // Phase 4: Execute navigation via action executors
    await ExecuteNavigationAsync(stepDef, windowHandle, templateMatch, ...);

    // Phase 5: Post-navigation delay
    if (stepDef.EstimatedDurationSeconds > 0)
    {
        await Task.Delay(TimeSpan.FromSeconds(...));
    }
}
```

**Execution Phases:**
1. **Validate** - Check step definition is valid
2. **Discover Window** - Find PlayOnline window handle
3. **Detect Screen** - Template matching (if configured)
4. **Execute Navigation** - Route actions to executors
5. **Delay** - Wait for screen transition

---

### ExecuteNavigationAsync() - Action Executor Routing

**Location:** `Services/AutoLogin/DynamicWorkflowHandler.cs:277-347`

```csharp
private async Task<bool> ExecuteNavigationAsync(...)
{
    var navigation = stepDef.Navigation;

    // Detection-only step (no navigation)
    if (navigation == null || navigation.Sequence.Count == 0)
        return true;

    // Build execution context
    var context = new WorkflowActionContext
    {
        WindowHandle = windowHandle,
        TemplateMatch = templateMatch,  // From Phase 3 detection
        Subtask = subtask,
        WorkflowStep = stepDef
    };

    // Execute each action in sequence
    for (int i = 0; i < navigation.Sequence.Count; i++)
    {
        var action = navigation.Sequence[i];

        // Route to appropriate executor via factory
        var executor = _actionExecutorFactory.GetExecutor(action.Action);

        // Execute action
        var success = await executor.ExecuteAsync(action, context, cancellationToken);

        if (!success)
            throw new InvalidOperationException($"Action '{action.Action}' failed");
    }

    // Post-navigation delay
    if (navigation.PostNavigationDelayMs > 0)
        await Task.Delay(navigation.PostNavigationDelayMs);

    return true;
}
```

**Action Executor Pattern:**
- Factory routes based on `action.Action` string (e.g., "Tab", "Click", "InputPassword")
- Each executor is specialized (InputPasswordActionExecutor, CharacterSlotActionExecutor, etc.)
- Template match from Phase 3 passed to executors via context
- Executors can perform their own template detection if needed

---

## Example: Default Workflow Execution with POL Proxy

### Scenario
- User has POL Proxy configured and running
- User adds character to queue using `playonline-standard.json` workflow

### Phase 1: Task Building (Queue Creation)

```
WorkflowTaskBuilder.CreateConditionEvaluatorAsync()
  ↓
ExternalApplicationService.GetApplicationsAsync()
  ↓
Running applications: ["POL Proxy", "Windower"]
  ↓
Create evaluator with snapshot: runningAppNames = {"POL Proxy", "Windower"}
```

### Step-by-Step Evaluation

**Workflow Steps (from playonline-standard.json):**

#### Step 0: Launch POL Proxy
```json
{
  "StepId": "launch_pol_proxy",
  "DisplayName": "Launch POL Proxy",
  "IsEnabled": true,
  "IsOptional": true,
  "Navigation": { "Sequence": [{ "Action": "Launch", ... }] }
}
```
**Evaluation:**
- ✅ IsEnabled = true
- ✅ SkipIfApplicationRunning = null (not configured)
- ✅ Condition = null
- **Result:** ✅ **EXECUTE** (Launch action will skip if already running via AllowSkipIfRunning parameter)

#### Step 1: Launch Windower
```json
{
  "StepId": "launch_windower",
  "DisplayName": "Launch Windower",
  "IsEnabled": true,
  "IsOptional": false
}
```
**Evaluation:**
- ✅ IsEnabled = true
- ✅ SkipIfApplicationRunning = null
- ✅ Condition = null
- **Result:** ✅ **EXECUTE**

#### Step 2: Member Selection
```json
{
  "StepId": "pol_member_selection",
  "DisplayName": "PlayOnline Member Selection",
  "IsEnabled": true
}
```
**Evaluation:**
- ✅ IsEnabled = true
- ✅ SkipIfApplicationRunning = null
- ✅ Condition = null
- **Result:** ✅ **EXECUTE**

#### Steps 3-8: PlayOnline Authentication
All execute (no skip conditions)

#### Step 9: Play Screen Navigation
```json
{
  "StepId": "pol_play_screen",
  "DisplayName": "Navigate to Play Screen",
  "IsEnabled": true,
  "SkipIfApplicationRunning": "POL Proxy"
}
```
**Evaluation:**
- ✅ IsEnabled = true
- ⚠️ SkipIfApplicationRunning = "POL Proxy"
- ⚠️ runningAppNames.Contains("POL Proxy") = **TRUE**
- **Result:** ❌ **SKIP** → Log: "Skipping step 'Navigate to Play Screen' because 'POL Proxy' is running"

#### Step 10: Play Confirmation
```json
{
  "StepId": "pol_play_confirmation",
  "DisplayName": "Confirm Play Selection",
  "IsEnabled": true,
  "SkipIfApplicationRunning": "POL Proxy"
}
```
**Evaluation:**
- ✅ IsEnabled = true
- ⚠️ SkipIfApplicationRunning = "POL Proxy"
- ⚠️ runningAppNames.Contains("POL Proxy") = **TRUE**
- **Result:** ❌ **SKIP** → Log: "Skipping step 'Confirm Play Selection' because 'POL Proxy' is running"

#### Steps 11-14: FFXI Character Login
All execute (no skip conditions)

---

### Final Subtask List (Sent to Executor)

```
✅ Step 0: Launch POL Proxy
✅ Step 1: Launch Windower
✅ Step 2: Member Selection
✅ Step 3: Login Information Screen
✅ Step 4: Connect Screen
✅ Step 5: Virtual Keyboard
✅ Step 6: Password Confirmation
✅ Step 7: OTP Entry (conditional on Account.IsOTPEnabled)
✅ Step 8: Connect Button
❌ Step 9: Play Screen (SKIPPED - POL Proxy running)
❌ Step 10: Play Confirmation (SKIPPED - POL Proxy running)
✅ Step 11: FFXI Terms Acceptance
✅ Step 12: FFXI Main Menu
✅ Step 13: Character Slot Selection
✅ Step 14: Character Confirmation
```

**Total:** 13 subtasks created (2 steps skipped)

---

### Phase 2 & 3: Execution

```
QueueExecutionOrchestrator → AutoLoginTaskExecutor
  ↓
For each subtask (only 13 subtasks):
  ↓
  LoginTaskHandlerResolver.ResolveHandler(subtask)
    ↓ (subtask.WorkflowStep != null)
  DynamicWorkflowHandler.ExecuteHandlerLogicAsync(subtask)
    ↓
  ExecuteNavigationStepAsync()
    ↓
    [Validate → Discover Window → Detect Screen → Execute Navigation → Delay]
    ↓
  ExecuteNavigationAsync()
    ↓
    For each action in navigation.Sequence:
      ↓
      WorkflowActionExecutorFactory.GetExecutor(action.Action)
        ↓
        Route to: LaunchActionExecutor, KeyboardActionExecutor,
                  InputPasswordActionExecutor, etc.
      ↓
      executor.ExecuteAsync(action, context, cancellationToken)
```

**Key Points:**
- ✅ DynamicWorkflowHandler **never sees Steps 9 & 10** (filtered in Phase 1)
- ✅ No runtime skip checks - handler just executes what it receives
- ✅ Completely data-driven - no hardcoded application logic

---

## Verification Checklist

### ✅ Skip Logic Placement
- [x] Skip logic happens in **WorkflowTaskBuilder** (task building phase)
- [x] Skip logic is **FIRST check** in condition evaluator (before Condition property)
- [x] Skipped steps **never become subtasks**
- [x] Skipped steps **never reach DynamicWorkflowHandler**

### ✅ No Hardcoded Logic in DynamicWorkflowHandler
- [x] No `if (applicationName == "POL Proxy")` checks
- [x] No conditional branching based on external application state
- [x] No step filtering or skipping during execution
- [x] 100% data-driven from WorkflowStepDefinition

### ✅ Execution Flow Integrity
- [x] Phase 1 (Task Building) → Filter & convert to subtasks
- [x] Phase 2 (Queue Execution) → Execute filtered subtasks
- [x] Phase 3 (Handler Execution) → Route actions to executors
- [x] Clear separation of concerns: Builder = Planning, Handler = Execution

### ✅ Data-Driven Architecture
- [x] Application names from `ExternalApplicationData` settings
- [x] Skip logic from `WorkflowStepDefinition.SkipIfApplicationRunning`
- [x] Navigation from `WorkflowStepDefinition.Navigation`
- [x] Actions from `NavigationAction.Sequence`
- [x] All routing via factory pattern (no hardcoded executors)

---

## Architectural Strengths

1. **Single Responsibility:**
   - **WorkflowTaskBuilder** = Planning & Filtering
   - **DynamicWorkflowHandler** = Execution
   - **Action Executors** = Specialized operations

2. **Data-Driven:**
   - All configuration in JSON workflow files
   - No code changes needed for new skip logic
   - Users can customize workflows in UI

3. **Predictable:**
   - Skip logic evaluated once at queue creation
   - Execution is deterministic (no mid-flight changes)
   - Clear audit trail in logs

4. **Extensible:**
   - New action executors via DI + factory
   - New skip conditions via WorkflowStepDefinition properties
   - New workflows via JSON files

---

## Potential Edge Cases

### Edge Case 1: Application Starts After Queue Creation
**Scenario:** User adds character to queue (POL Proxy not running), then starts POL Proxy before execution.

**Behavior:**
- ✅ Skip check happens at queue creation time (POL Proxy not running)
- ✅ Steps 9 & 10 **WILL EXECUTE** (not filtered out)
- ⚠️ User will navigate play screen unnecessarily

**Solution:**
- Document that skip logic is snapshot-based
- OR: Add runtime skip check in DynamicWorkflowHandler (future enhancement)

### Edge Case 2: Application Crashes During Execution
**Scenario:** POL Proxy running at queue creation, crashes during Step 5 execution.

**Behavior:**
- ✅ Steps 9 & 10 remain skipped (filtered during task building)
- ⚠️ FFXI launch may fail (POL Proxy expected but not running)

**Solution:**
- Launch actions already handle process detection
- Template detection timeout will catch missing screens

---

## Recommendations

### ✅ Current Architecture is Solid
- Skip logic is correctly placed in task building phase
- No hardcoded bypasses in DynamicWorkflowHandler
- Clean separation of concerns
- Fully data-driven

### Optional Enhancements (Future)

1. **Runtime Skip Checks:**
   - Add optional runtime revalidation of SkipIfApplicationRunning
   - Check at step execution time, not just task building
   - Trade-off: More flexible vs. less predictable

2. **Skip Logic Audit Log:**
   - Log all skip decisions with reasons
   - Help users debug why steps were skipped
   - Already partially implemented (line 142 in WorkflowTaskBuilder)

3. **Dynamic Skip UI Indicator:**
   - Show which steps will be skipped in queue preview
   - Update if application state changes before execution
   - Requires UI changes

---

## Conclusion

**The skip logic is correctly implemented:**
- ✅ Evaluated during task building (WorkflowTaskBuilder)
- ✅ No hardcoded logic in DynamicWorkflowHandler
- ✅ Completely data-driven from workflow JSON
- ✅ Clear separation: Planning (Builder) vs Execution (Handler)

**The execution flow is clean:**
- Phase 1: Filter steps → Create subtasks
- Phase 2: Execute subtasks via handlers
- Phase 3: Route actions via executors

**The architecture supports:**
- User-customizable skip logic via UI
- No code changes for new workflows
- Predictable, deterministic execution
- Clear audit trail in logs
