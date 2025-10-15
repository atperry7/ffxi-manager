# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Development Commands

### Build and Run
```bash
# Build the solution
dotnet build

# Build for release
dotnet build --configuration Release

# Run the application
dotnet run --configuration Release

# Clean build artifacts
dotnet clean
```

### Testing
```bash
# Run all tests
dotnet test Testing/FFXIManager.Tests.csproj

# Run tests with specific configuration
dotnet test Testing/FFXIManager.Tests.csproj --configuration Release

# Build test project only
dotnet build Testing/FFXIManager.Tests.csproj
```

### Publishing
```bash
# Publish for deployment
dotnet publish -c Release -o ./publish

# Create ZIP package (PowerShell)
Compress-Archive -Path "./publish/*" -DestinationPath "FFXIManager-local.zip"
```

## Architecture Overview

### Core Architecture Patterns
- **MVVM Pattern**: Clean separation between Views (XAML), ViewModels (business logic), and Models (data)
- **Dependency Injection**: All services registered in `Infrastructure/DependencyInjection.cs` using Microsoft.Extensions.DependencyInjection
- **Service Layer**: Business logic separated into focused service interfaces with implementations
- **Interface-based Design**: All major components have interfaces for testability and maintainability

### Key Architectural Components

#### Dependency Injection Container
- Main registration in `Infrastructure/DependencyInjection.cs:AddAppServices()`
- Services registered as Singletons for application-wide state management
- Clear separation of concerns: Core services, UI/Threading, Process/Monitoring, App logic

#### Service Layer Organization
- **Core Services**: Settings, Configuration, Logging, Caching, Notifications, Validation
- **UI/Threading**: WpfUiDispatcher for thread-safe UI updates, WindowEventTracker
- **Process/Monitoring**: Process utilities, unified monitoring, PlayOnline monitoring
- **Application Logic**: External applications, status messages, character ordering, hotkeys

#### ViewModel Architecture
- `MainViewModel` acts as coordinator, injecting dependencies into specialized ViewModels
- Specialized ViewModels: `ProfileManagementViewModel`, `ApplicationManagementViewModel`, `PlayOnlineMonitorViewModel`
- Base class `ViewModelBase` provides common MVVM functionality
- Clear dependency injection pattern with null checks

#### Service Interface Patterns
All services follow consistent interface patterns:
- Interfaces prefixed with `I` (e.g., `ISettingsService`, `IProfileService`)
- Services focused on single responsibility
- Clear separation between interfaces and implementations

### Technology Stack
- **.NET 9** with Windows-specific features
- **WPF** for UI with XAML views
- **Serilog** for comprehensive logging with multiple sinks (Console, File, Async)
- **SharpDX.DirectInput** for controller support
- **System.Management** for system-level operations
- **Microsoft.Extensions** for hosting and dependency injection

### Project Structure
- `Services/`: Business logic services with interface-based design
- `ViewModels/`: MVVM ViewModels with Base classes
- `Views/`: WPF XAML views and code-behind
- `Infrastructure/`: Cross-cutting concerns (DI, threading, process management)
- `Configuration/`: Application configuration services
- `Testing/`: MSTest-based test project

### Auto-Login Module
- Located in `Services/AutoLogin/` with Application layer
- Uses event-driven architecture with EventBus pattern
- Implements comprehensive security validation framework
- Includes integration testing with quickstart validation

#### Auto-Login Handler Architecture (Refactored)
The Auto-Login handlers follow a **service-oriented architecture** with clear separation of concerns:

**Handler Pattern**:
- Handlers inherit from `BaseLoginTaskHandler` for common infrastructure
- Handlers orchestrate workflows by delegating to specialized services
- Each handler focuses on coordination rather than implementation details
- Target: Methods should be <80 lines following SOLID principles

**Service Extraction Pattern**:
- **Navigation Services**: Handle UI navigation and screen transitions
  - Example: `PlayOnlineNavigationService` - Manages PlayOnline → FFXI navigation flow
  - Reusable across handlers that need similar navigation patterns

- **Authentication Services**: Handle credential validation and secure operations
  - Example: `PlayOnlineAuthenticationService` - Password/OTP validation and retrieval
  - Provides security-conscious logging without exposing sensitive data

- **Specialized Services**: Domain-specific operations extracted into focused services
  - Screen detection, template matching, UI automation
  - Process management, window handling, monitoring

**Key Refactoring (PlayOnlineAuthHandler)**:
- **Before**: 1288 lines with complex 200+ line methods
- **After**: ~1000 lines with focused <80 line methods
- **Extracted**: ~550 lines into 2 specialized services
- **Result**: 22% reduction in handler size, dramatically improved maintainability

**Refactored Handlers**:
- ✅ `PlayOnlineAuthHandler` - Refactored with service extraction pattern
- 🔄 `WindowerLaunchHandler` - Candidate for similar refactoring
- 🔄 `FFXIGameHandler` - Candidate for similar refactoring
- 🔄 `POLProxyLaunchHandler` - Candidate for similar refactoring

See [Handler Refactoring Guide](#handler-refactoring-guide) below for applying these patterns to other handlers.

## Development Guidelines

### Code Conventions
- Follow `.editorconfig` settings: 4-space indentation, PascalCase for public members
- Use file-scoped namespaces (`csharp_style_namespace_declarations = file_scoped`)
- Prefer explicit types over `var` except when type is apparent
- Enable nullable reference types project-wide

### Service Development
- Always create interface first, then implementation
- Register services in `Infrastructure/DependencyInjection.cs`
- Follow constructor injection pattern with null checks
- Use `ILogger<T>` for logging within services

### Testing
- Use MSTest framework (`MSTest.TestFramework`, `MSTest.TestAdapter`)
- Test project targets same framework as main project (net9.0-windows)
- Comprehensive security testing framework in place for auto-login features

### Version Management
- Version controlled in `FFXIManager.csproj` (currently 1.3.1-beta)
- Do not manually bump version numbers - handled by release workflow
- Strong name signing enabled with `ffximanager.snk`

### Build Configuration
- .NET Analyzers enabled with latest analysis level
- Warnings treated as errors
- Automatic exclusion of Testing files from main build
- Application manifests and configuration files copied to output

## Handler Refactoring Guide

This guide documents the proven patterns for refactoring Auto-Login handlers, based on the successful `PlayOnlineAuthHandler` refactoring.

### When to Refactor a Handler

Refactor when a handler exhibits these characteristics:
- **Size**: File exceeds 800 lines
- **Method Complexity**: Methods exceed 80 lines
- **Responsibilities**: Handler does too many things (navigation + authentication + validation + ...)
- **Reusability**: Logic could be shared with other handlers
- **Testability**: Difficult to test in isolation

### Refactoring Process Overview

**Phase 1: Analysis** (Identify extraction candidates)
**Phase 2: Service Creation** (Extract cohesive functionality)
**Phase 3: Integration** (Update handler to use services)
**Phase 4: Verification** (Build, test, validate)

### Phase 1: Analysis

#### Step 1.1: Read and Understand the Handler
```bash
# Read the entire handler to understand its responsibilities
Read Services/AutoLogin/YourHandler.cs
```

Look for:
- Distinct logical sections (authentication, navigation, validation)
- Repeated patterns across methods
- Long methods (>80 lines) that do multiple things
- Code that could be reused by other handlers

#### Step 1.2: Identify Service Boundaries

**Good Service Candidates**:
- **Navigation Logic**: Screen detection, UI interaction, window transitions
  - Example: All methods that navigate between screens
  - Pattern: Methods with "Navigate", "Wait", "Detect" in their names

- **Authentication Logic**: Credential validation, password/OTP operations
  - Example: Password retrieval, OTP generation, validation methods
  - Pattern: Methods that interact with `IWindowsCredentialsService` or `IOTPService`

- **Screen Transition Logic**: Window handle management, process transitions
  - Example: Methods that manage window handles across process boundaries
  - Pattern: Methods that track PIDs, detect window changes

- **Validation Logic**: Configuration checks, requirement validation
  - Example: Methods that validate account settings, prerequisites
  - Pattern: Methods that start with "Validate" or check configuration

**Service Naming Convention**:
- Navigation: `{Context}NavigationService` (e.g., `PlayOnlineNavigationService`)
- Authentication: `{Context}AuthenticationService` (e.g., `PlayOnlineAuthenticationService`)
- Screen Transition: `{Context}TransitionService` (e.g., `WindowTransitionService`)
- Validation: `{Context}ValidationService` (e.g., `AccountValidationService`)

#### Step 1.3: Document Extraction Plan

Create a checklist:
```markdown
## Extraction Plan for {HandlerName}

### Services to Create:
1. **{ServiceName1}** (~XXX lines)
   - Method1
   - Method2
   - Method3

2. **{ServiceName2}** (~XXX lines)
   - Method1
   - Method2

### Handler Changes:
- Add service dependencies to constructor
- Replace method calls with service calls
- Remove extracted methods
- Update documentation

### Expected Results:
- Handler: {Current} lines → ~{Target} lines
- Extracted: ~{Total} lines into {Count} services
- Reduction: {Percentage}%
```

### Phase 2: Service Creation

#### Step 2.1: Create Service Interface

**Location**: `Services/AutoLogin/{ServiceName}/I{ServiceName}.cs` or `Services/AutoLogin/I{ServiceName}.cs`

**Template**:
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Interface for {service purpose}.
    /// {Brief description of what this service does}
    /// </summary>
    public interface I{ServiceName}
    {
        /// <summary>
        /// {Method description}
        /// </summary>
        /// <param name="paramName">Description</param>
        /// <returns>Description of return value</returns>
        Task<ReturnType> MethodNameAsync(ParamType paramName, CancellationToken cancellationToken);
    }
}
```

**Best Practices**:
- Keep interfaces focused (Single Responsibility)
- Use async methods with CancellationToken support
- Document all parameters and return values
- Consider what other handlers might need from this service

#### Step 2.2: Create Service Implementation

**Location**: Same directory as interface

**Template**:
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Service responsible for {primary responsibility}.
    /// {Detailed description of service purpose and capabilities}
    ///
    /// This service wraps:
    /// - {Dependency1}: For {purpose}
    /// - {Dependency2}: For {purpose}
    ///
    /// Key features:
    /// - {Feature 1}
    /// - {Feature 2}
    /// </summary>
    public class {ServiceName} : I{ServiceName}
    {
        private readonly IDependency1 _dependency1;
        private readonly IDependency2 _dependency2;
        private readonly ILoggingService _loggingService;

        public {ServiceName}(
            IDependency1 dependency1,
            IDependency2 dependency2,
            ILoggingService loggingService)
        {
            _dependency1 = dependency1 ?? throw new ArgumentNullException(nameof(dependency1));
            _dependency2 = dependency2 ?? throw new ArgumentNullException(nameof(dependency2));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        // Implement interface methods here
        // Copy extracted methods from handler
        // Make them async if needed
        // Add comprehensive logging
        // Keep methods focused (<80 lines)
    }
}
```

**Service Implementation Checklist**:
- ✅ All dependencies injected via constructor
- ✅ Null checks for all dependencies
- ✅ Comprehensive XML documentation
- ✅ Security-conscious logging (mask sensitive data)
- ✅ Methods are focused and <80 lines
- ✅ Async/await pattern used correctly
- ✅ CancellationToken support throughout
- ✅ Proper exception handling

#### Step 2.3: Copy and Adapt Methods

**Extraction Process**:
1. **Copy method signature** from handler to service
2. **Update visibility** to public (was private in handler)
3. **Add to interface** if it should be publicly accessible
4. **Adapt parameters**:
   - Remove handler-specific parameters (e.g., subtask if not needed)
   - Keep essential parameters (window handles, accounts, cancellation tokens)
   - Consider whether subtask is needed for progress reporting
5. **Update logging**:
   - Use service's `_loggingService` instance
   - Add contextual information
   - Ensure security (mask passwords, OTP codes)
6. **Remove handler-specific calls**:
   - Replace base class methods with direct service calls if needed
   - Ensure service remains independent of handler

**Example - Before (Handler)**:
```csharp
private async Task<string> GenerateSecureOTPCodeAsync(
    AutoLoginSubtask subtask,
    AutoLoginQueueItem queueItem,
    string accountName,
    CancellationToken cancellationToken)
{
    await UpdateProgressWithPhaseAsync(subtask, "authentication", 50, "Generating OTP");

    var otpCode = await _otpService.GenerateOTPCodeAsync(
        queueItem.Profile?.FilePath ?? string.Empty,
        queueItem.Account.Id);

    if (string.IsNullOrEmpty(otpCode))
    {
        throw new InvalidOperationException($"Could not generate OTP code for {accountName}");
    }

    // SECURITY: Mask OTP in logs
    await _loggingService.LogDebugAsync($"Generated OTP: {otpCode.Substring(0, 2)}****");

    return otpCode;
}
```

**Example - After (Service)**:
```csharp
public async Task<string?> GenerateSecureOTPCodeAsync(
    PlayOnlineMemberAccount account,
    string profileFilePath)
{
    if (account == null)
        throw new ArgumentNullException(nameof(account));

    if (!account.IsOTPEnabled)
    {
        await _loggingService.LogWarningAsync($"OTP generation requested but not enabled for: {account.AccountName}");
        return null;
    }

    var otpCode = await _otpService.GenerateOTPCodeAsync(profileFilePath, account.Id);

    if (string.IsNullOrEmpty(otpCode))
    {
        await _loggingService.LogWarningAsync($"Could not generate OTP code for: {account.AccountName}");
        return null;
    }

    // SECURITY: Log OTP generation without exposing full code
    var maskedCode = otpCode.Length >= 2 ? $"{otpCode.Substring(0, 2)}****" : "****";
    await _loggingService.LogDebugAsync($"Generated OTP for {account.AccountName}: {maskedCode} (Length: {otpCode.Length})");

    return otpCode;
}
```

**Key Changes**:
- ✅ Removed `subtask` parameter (progress reporting stays in handler)
- ✅ Simplified parameters to essentials
- ✅ Added null checks and validation
- ✅ Enhanced logging with more context
- ✅ Made public for service interface
- ✅ Improved error handling

### Phase 3: Integration

#### Step 3.1: Register Services in DI Container

**Location**: `Infrastructure/DependencyInjection.cs`

**Process**:
1. Add using statement for service namespace
2. Register service in `AddAppServices()` method
3. Use appropriate lifetime (typically Singleton for stateless services)

**Example**:
```csharp
// At top of file
using FFXIManager.Services.AutoLogin.Navigation;

// In AddAppServices() method, in logical grouping
// AutoLogin support services (refactored for SOLID principles)
services.AddSingleton<IPlayOnlineNavigationService, Services.AutoLogin.Navigation.PlayOnlineNavigationService>();
services.AddSingleton<IPlayOnlineAuthenticationService, PlayOnlineAuthenticationService>();
services.AddSingleton<IYourNewService, YourNewService>();
```

**Service Lifetime Guidelines**:
- **Singleton**: Stateless services, shared across application (most AutoLogin services)
- **Scoped**: Request/operation-scoped state (rare in AutoLogin)
- **Transient**: New instance per request (avoid for services)

#### Step 3.2: Update Handler Constructor

**Process**:
1. Add private readonly fields for new services
2. Add parameters to constructor
3. Add null checks and assignment

**Example**:
```csharp
public class YourHandler : BaseLoginTaskHandler
{
    // Existing dependencies...
    private readonly ILoggingService _loggingService;
    private readonly IUIAutomationService _automationService;

    // NEW: Add service fields
    private readonly IYourNavigationService _navigationService;
    private readonly IYourAuthenticationService _authenticationService;

    public YourHandler(
        ILoggingService loggingService,
        IScreenshotCaptureService screenshotService,
        ITemplateMatchingService templateService,
        ITemplateManagementService templateManagementService,
        IUIAutomationService automationService,
        // NEW: Add service parameters
        IYourNavigationService navigationService,
        IYourAuthenticationService authenticationService)
        : base(loggingService, screenshotService, templateService, templateManagementService)
    {
        _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
        // NEW: Add null checks and assignment
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
    }
}
```

#### Step 3.3: Replace Method Calls

**Process**:
1. Find all calls to extracted methods
2. Replace with service calls
3. Update parameters as needed
4. Handle return values appropriately

**Example - Before**:
```csharp
// OLD: Direct method call
var password = await RetrieveSecurePasswordAsync(subtask, queueItem, windowHandle, accountName, cancellationToken);
```

**Example - After**:
```csharp
// NEW: Service call
var password = await _authenticationService.RetrieveSecurePasswordAsync(
    queueItem.Account!,
    queueItem.Profile?.FilePath ?? string.Empty);
```

**Common Patterns**:
- **Progress Reporting**: Keep in handler, service focuses on logic
- **Error Handling**: Service returns null/throws, handler logs/reports
- **Context Management**: Handler manages context, service performs operations

#### Step 3.4: Remove Extracted Methods

**Process**:
1. Verify all calls to extracted methods have been replaced
2. Delete the extracted method definitions from handler
3. Update handler documentation to reflect new architecture

**Verification**:
```bash
# Search for any remaining calls to extracted methods
# Should return no results
Grep "OldMethodName" Services/AutoLogin/YourHandler.cs
```

#### Step 3.5: Update Handler Documentation

Update class-level documentation:
```csharp
/// <summary>
/// Handles {primary responsibility} for the AutoLogin process.
/// Orchestrates {workflow description} by delegating to specialized services.
///
/// **Refactoring Improvements:**
/// - Extracted navigation logic to {NavigationService}
/// - Extracted authentication logic to {AuthenticationService}
/// - Methods decomposed for single responsibility
/// - Improved testability and maintainability
/// </summary>
/// <remarks>
/// **Dependencies:**
/// - {NavigationService}: {Purpose}
/// - {AuthenticationService}: {Purpose}
/// - {OtherService}: {Purpose}
///
/// **Architecture:**
/// Handler focuses on orchestration and progress reporting.
/// Business logic delegated to specialized, testable services.
/// Follows SOLID principles with clear separation of concerns.
/// </remarks>
```

### Phase 4: Verification

#### Step 4.1: Build Verification

```bash
# Clean build
dotnet clean

# Build with minimal verbosity
dotnet build --verbosity minimal
```

**Expected Result**: ✅ Build successful with 0 errors

**Common Issues**:
- **Missing using statements**: Add namespace imports
- **Null reference warnings**: Add null checks or null-forgiving operators where appropriate
- **Type mismatches**: Verify method signatures match between handler and service

#### Step 4.2: Code Review Checklist

**Handler Review**:
- ✅ File size reduced significantly (aim for >20% reduction)
- ✅ All methods <80 lines
- ✅ Clear orchestration logic, minimal implementation
- ✅ Proper service dependency injection
- ✅ Updated documentation

**Service Review**:
- ✅ Single, focused responsibility
- ✅ All dependencies injected
- ✅ Comprehensive logging
- ✅ Security-conscious (no credential exposure)
- ✅ Proper error handling
- ✅ Methods <80 lines

**DI Registration Review**:
- ✅ All services registered
- ✅ Correct lifetimes (Singleton for stateless)
- ✅ Proper using statements
- ✅ Services in logical grouping

#### Step 4.3: Integration Testing

**Test Scenarios**:
1. **Happy Path**: Normal flow works end-to-end
2. **Error Handling**: Failures are caught and logged appropriately
3. **Cancellation**: CancellationToken properly respected
4. **Edge Cases**: Null values, missing config, timeouts handled

**Testing Approach**:
```markdown
## Integration Test Plan

### Test 1: Normal Flow
- Setup: Valid configuration, credentials available
- Expected: Handler completes successfully, all phases execute
- Verify: Logs show service calls, progress reporting works

### Test 2: Missing Credentials
- Setup: Account without stored password
- Expected: Validation fails gracefully with clear error
- Verify: AuthenticationService logs warning, handler reports error

### Test 3: Cancellation
- Setup: Start operation, cancel during execution
- Expected: Operation cancels cleanly
- Verify: No orphaned processes, proper cleanup

### Test 4: Service Failure
- Setup: Simulate service failure (bad template, timeout)
- Expected: Handler catches exception, logs error, reports failure
- Verify: Error message is user-friendly, includes troubleshooting info
```

#### Step 4.4: Performance Verification

**Metrics to Check**:
- Execution time should be similar or improved
- Memory usage should be comparable
- No new memory leaks introduced

**Simple Performance Test**:
```csharp
// Time the operation before and after refactoring
var stopwatch = Stopwatch.StartNew();
await handler.ExecuteAsync(subtask, queueItem, context, cancellationToken);
stopwatch.Stop();
_loggingService.LogInfoAsync($"Execution time: {stopwatch.ElapsedMilliseconds}ms");
```

### Common Patterns and Best Practices

#### Pattern 1: Phase-Based Method Extraction

**Before** - One large method:
```csharp
private async Task ExecuteLargeWorkflowAsync(...) // 200 lines
{
    // Phase 1: Validation (30 lines)
    // Phase 2: Navigation (50 lines)
    // Phase 3: Authentication (60 lines)
    // Phase 4: Confirmation (40 lines)
    // Phase 5: Cleanup (20 lines)
}
```

**After** - Multiple focused methods:
```csharp
private async Task ExecuteWorkflowAsync(...) // 20 lines
{
    // Phase 1: Validation
    await ValidateConfigurationAsync(...);

    // Phase 2: Navigation
    var windowHandle = await _navigationService.NavigateToTargetAsync(...);

    // Phase 3: Authentication
    await _authenticationService.AuthenticateAsync(...);

    // Phase 4: Confirmation
    await ConfirmCompletionAsync(...);

    // Phase 5: Cleanup
    await CleanupResourcesAsync(...);
}

// Each phase method is <30 lines, focused on single concern
```

#### Pattern 2: Service Method Composition

Services can call other services:
```csharp
public class NavigationService : INavigationService
{
    private readonly IScreenDetectionService _screenDetection;
    private readonly IUIAutomationService _automation;

    public async Task<IntPtr> NavigateToScreenAsync(...)
    {
        // Use screen detection service
        var match = await _screenDetection.DetectScreenAsync(...);

        // Use automation service
        await _automation.ClickAsync(...);

        return newWindowHandle;
    }
}
```

#### Pattern 3: Progress Reporting Delegation

Handler keeps progress reporting responsibility:
```csharp
// In Handler
private async Task ExecutePhaseAsync(AutoLoginSubtask subtask, ...)
{
    // Handler reports progress
    await UpdateProgressWithPhaseAsync(subtask, "authentication", 25, "Starting authentication");

    // Service does the work
    var result = await _authenticationService.AuthenticateAsync(...);

    // Handler reports completion
    await UpdateProgressWithPhaseAsync(subtask, "authentication", 50, "Authentication completed");

    return result;
}
```

#### Pattern 4: Security-Conscious Logging

Always mask sensitive data in logs:
```csharp
// BAD - Exposes password
await _loggingService.LogDebugAsync($"Password: {password}");

// GOOD - Masks sensitive data
await _loggingService.LogDebugAsync($"Password retrieved: Length={password.Length} characters");

// BAD - Exposes full OTP
await _loggingService.LogDebugAsync($"OTP: {otpCode}");

// GOOD - Partial mask with metadata
var maskedOtp = otpCode.Length >= 2 ? $"{otpCode.Substring(0, 2)}****" : "****";
await _loggingService.LogDebugAsync($"OTP generated: {maskedOtp} (Length: {otpCode.Length})");
```

### Refactoring Checklist Template

Use this checklist for each handler refactoring:

```markdown
## Refactoring Checklist: {HandlerName}

### Pre-Refactoring
- [ ] Handler analyzed and understood
- [ ] Service boundaries identified
- [ ] Extraction plan documented
- [ ] Expected results defined

### Service Creation
- [ ] Interface created with documentation
- [ ] Implementation created with dependencies
- [ ] Methods extracted and adapted
- [ ] Logging added (security-conscious)
- [ ] All methods <80 lines
- [ ] Unit tests created (optional but recommended)

### Integration
- [ ] Services registered in DependencyInjection.cs
- [ ] Using statements added
- [ ] Handler constructor updated
- [ ] Service fields added with null checks
- [ ] Method calls replaced with service calls
- [ ] Extracted methods removed from handler
- [ ] Handler documentation updated

### Verification
- [ ] Build successful (0 errors)
- [ ] All methods <80 lines
- [ ] File size reduced >20%
- [ ] Integration tests pass
- [ ] Normal flow works
- [ ] Error handling works
- [ ] Cancellation works
- [ ] Performance acceptable

### Documentation
- [ ] CLAUDE.md updated
- [ ] Service documentation complete
- [ ] Handler documentation complete
- [ ] Architecture diagrams updated (if applicable)
```

### Success Metrics

After refactoring, you should see:

**Quantitative**:
- Handler file size reduced by >20%
- All methods <80 lines
- Extracted code >400 lines into focused services
- Build time unchanged or improved
- Test coverage maintained or improved

**Qualitative**:
- Code is more readable and maintainable
- Service responsibilities are clear
- Handler orchestration is obvious
- Testing is easier (services can be mocked)
- Reusability improved (services usable by other handlers)
- SOLID principles adhered to

### Next Handler Candidates

**Priority Order** (based on complexity and reuse potential):

1. **WindowerLaunchHandler** (~700 lines)
   - Extract: Windower process management service
   - Extract: Addon configuration service
   - Benefit: Process management reusable by other handlers

2. **FFXIGameHandler** (~600 lines)
   - Extract: FFXI navigation service
   - Extract: Character selection service
   - Benefit: Game-specific logic isolated for testing

3. **POLProxyLaunchHandler** (~500 lines)
   - Extract: POL Proxy detection service (may merge with existing)
   - Extract: Proxy configuration service
   - Benefit: Proxy logic reusable, clearer flow

### Resources

**Reference Implementations**:
- `Services/AutoLogin/PlayOnlineAuthHandler.cs` - Refactored handler example
- `Services/AutoLogin/Navigation/PlayOnlineNavigationService.cs` - Navigation service example
- `Services/AutoLogin/PlayOnlineAuthenticationService.cs` - Authentication service example
- `Infrastructure/DependencyInjection.cs` - Service registration example

**Patterns to Follow**:
- `WindowerLaunchHandler.cs` - Well-structured handler (pre-refactoring baseline)
- `BaseLoginTaskHandler.cs` - Base class infrastructure

**Key Files**:
- Handler location: `Services/AutoLogin/`
- Service location: `Services/AutoLogin/` or `Services/AutoLogin/{Context}/`
- DI registration: `Infrastructure/DependencyInjection.cs`
- Models: `Models/` (for shared types)

---

**Remember**: The goal is not just to reduce lines of code, but to improve **maintainability**, **testability**, and **reusability** while adhering to **SOLID principles**. Each extracted service should have a clear, single responsibility and be usable in isolation.