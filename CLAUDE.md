# FFXIManager - Claude Code Context

**Project**: FFXI Manager Desktop Application  
**Language**: C# (.NET Framework 4.8)  
**Architecture**: WPF MVVM with Clean Architecture principles  
**Current Feature**: Fresh Auto-Login Architecture Implementation

## Recent Changes (Last 3 Updates)

### 2025-09-12: Auto-Login Architecture Specification
- Created comprehensive feature specification for auto-login redesign
- Defined domain-driven design approach with clean architecture layers
- Established security-first credential management requirements
- Specified performance targets: <500ms UI updates, >95% success rate

### Previous: Structured Logging Migration  
- Converted string interpolations to structured logging across services
- Enhanced NotificationServiceEnhanced, ProfileService, ExternalApplicationService
- Improved DirectInputControllerService and PlayOnlineMonitorService logging

### Previous: Infrastructure Improvements
- Dependency injection refactoring in progress
- Service layer enhancements for maintainability
- Test coverage improvements

## Technology Stack

### Core Framework
- **Runtime**: .NET Framework 4.8
- **UI**: WPF with MVVM pattern
- **Testing**: MSTest framework
- **Logging**: Microsoft.Extensions.Logging (enhanced structured logging)

### Key Dependencies  
- **Windows APIs**: User32.dll, Advapi32.dll for automation
- **Security**: Windows Credential Manager for secure storage
- **Process Management**: System.Diagnostics.Process
- **Async Patterns**: Task-based async/await with CancellationToken

### Current Architecture
```
FFXIManager/
├── Services/           # Business logic and external integrations
├── ViewModels/         # MVVM presentation logic
├── Views/              # WPF UI components
├── Infrastructure/     # Cross-cutting concerns
├── Testing/           # Unit and integration tests
└── auto-login-research/ # Previous auto-login investigation
```

## Auto-Login Domain Model

### Core Entities
- **LoginSession**: Represents active auto-login attempt with state tracking
- **AccountConfiguration**: Immutable profile settings (POL member, character slot, launch method)
- **LoginCredentials**: Secure credential container with automatic disposal
- **WorkflowState**: Comprehensive enumeration of login workflow states
- **LoginContext**: Immutable context passed between workflow steps

### Value Objects
- **POLMemberSlot**: Type-safe POL member position (1-4)
- **CharacterSlot**: Type-safe character slot identifier (1-16)  
- **WindowHandle**: Native window management with validation
- **ScreenCoordinate**: DPI-aware coordinate system

### Key Interfaces
- **IAutoLoginService**: Public facade for UI integration
- **ILoginOrchestrator**: Core workflow execution coordinator
- **ILoginStepHandler**: Individual step execution contract
- **ICredentialStore**: Secure Windows Credential Manager integration
- **IProcessManager**: Window and process management utilities
- **IScreenDetector**: Visual state detection strategies

## Development Standards

### Code Quality Requirements
- **File Size Limit**: 300 lines maximum per class file
- **Function Size**: Small, focused functions with single responsibility
- **SOLID Principles**: Enforced throughout design
- **Error Handling**: Comprehensive with user-friendly messages
- **Security**: No credential exposure in logs or exceptions

### Testing Standards (NON-NEGOTIABLE)
- **TDD Approach**: Tests written first, must fail, then implement
- **Test Order**: Contract → Integration → E2E → Unit
- **Real Dependencies**: Use actual Windows Credential Manager, processes
- **Coverage Targets**: >80% for critical paths, 100% for security code
- **Memory Testing**: Leak detection and resource disposal validation

### Security Requirements
- **Credential Storage**: Windows Credential Manager with DPAPI encryption
- **Memory Protection**: SecureString usage, immediate disposal
- **Focus Validation**: Window focus verification before sensitive input
- **Audit Trail**: Security operations logged without exposing credentials
- **Access Control**: Current user scope only

## Performance Targets

### Response Requirements
- **UI Updates**: <500ms during active automation
- **Login Duration**: <120 seconds for complete workflow
- **Success Rate**: >95% under normal conditions
- **Memory Stability**: Zero leaks during extended sessions

### Scalability Considerations
- **Single Session**: One active login per account configuration
- **Resource Management**: Automatic cleanup and disposal
- **Event Handling**: Non-blocking UI thread operations
- **Concurrent Operations**: Thread-safe credential access

## Current Implementation Status

### Completed Phase 0 & 1 (Design)
- ✅ Architecture research and technology decisions
- ✅ Domain model design with comprehensive entities
- ✅ Interface contracts for all major components
- ✅ Security architecture with Windows Credential Manager
- ✅ Performance requirements and validation scenarios

### Ready for Phase 2 (Task Generation)
- **Next Step**: Create detailed implementation tasks
- **Focus Areas**: Domain layer first, then Application, Infrastructure, UI
- **Test Strategy**: TDD with contract tests, integration tests, security tests
- **Implementation Order**: Security and core domain objects priority

## Integration Points

### Existing Services (Preserve)
- **ProfileService**: Profile management and configuration
- **ExternalApplicationService**: Windower and external app launching  
- **NotificationServiceEnhanced**: User notifications and messaging
- **DirectInputControllerService**: Input automation foundations
- **PlayOnlineMonitorService**: POL process monitoring

### New Services (To Implement)
- **AutoLoginService**: Main facade for auto-login functionality
- **LoginOrchestrator**: Workflow state machine and step coordination
- **CredentialStore**: Secure Windows Credential Manager wrapper
- **StepHandlerFactory**: Dynamic step handler creation
- **SmartDelayService**: Adaptive timing and stabilization

## UI Integration Requirements

### ViewModels (New)
- **AutoLoginViewModel**: Main auto-login control interface
- **StepProgressViewModel**: Real-time step progress tracking
- **CredentialManagementViewModel**: Secure credential input/management
- **StepByStepControlViewModel**: Manual step control interface

### Views (New)  
- **AutoLoginView**: Primary auto-login UI panel
- **StepControlOverlay**: Progress and control overlay
- **CredentialPromptView**: Secure credential input dialog
- **OTPInputView**: One-time password entry interface

## Error Handling Strategy

### Exception Hierarchy
```csharp
AutoLoginException
├── CredentialException (credential access/validation failures)
├── WorkflowException (state machine and step execution failures)  
├── SecurityException (focus validation, window security failures)
├── ProcessException (window/process management failures)
└── ConfigurationException (invalid configuration or setup failures)
```

### Recovery Strategies
- **Transient Errors**: Exponential backoff retry with jitter
- **Security Errors**: Immediate abort with audit logging
- **Process Errors**: Process recovery and window rediscovery
- **User Errors**: Clear messaging with correction guidance

## Commit Strategy

### Phase Implementation Commits
1. **Domain Layer**: Entities, value objects, enums (with tests first)
2. **Application Layer**: Interfaces, use cases, orchestrators (with contract tests)
3. **Infrastructure Layer**: Windows integrations, security (with integration tests)
4. **UI Layer**: ViewModels, views, binding (with UI tests)
5. **Integration**: End-to-end testing and performance validation

### Commit Message Format
```
feat(domain): add LoginSession entity with state management

- Implement LoginSession with immutable state tracking
- Add comprehensive validation and business rules
- Include domain events for state transitions
- Add unit tests with 95% coverage

Tests pass: ✅ Domain tests
Follows TDD: ✅ Tests written first, failed, then implemented
Security review: ✅ No credential exposure
Memory tested: ✅ No leaks detected
```

## Known Constraints

### Technical Limitations
- **Windows Only**: Win32 API dependencies limit to Windows platform
- **DPI Scaling**: Screen detection must handle various DPI settings  
- **Game Updates**: UI patterns may change with game patches
- **Process Timing**: External application timing varies by system performance

### Security Boundaries  
- **User Scope**: Cannot access other users' credentials
- **Process Isolation**: Cannot interact with elevated processes
- **Network Security**: No credential transmission over network
- **Audit Requirements**: All credential operations must be auditable

## Development Workflow

### Feature Branch Strategy
- **Current Branch**: `feature/refactor-dependency-injection`
- **Auto-Login Branch**: `001-prd-md` (specification phase)
- **Integration**: Local commits until feature complete
- **Testing**: Comprehensive test suite before any merges

### Build and Test Commands
```bash
# Build solution
dotnet build

# Run all tests  
dotnet test

# Run specific test category
dotnet test --filter "Category=AutoLogin"

# Performance profiling
dotnet test --collect:"XPlat Code Coverage"
```

This context provides comprehensive guidance for implementing the fresh auto-login architecture while maintaining the existing FFXIManager codebase quality and security standards.

## Task Master AI Instructions
**Import Task Master's development workflow commands and guidelines, treat as if import is in the main CLAUDE.md file.**
@./.taskmaster/CLAUDE.md
