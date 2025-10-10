# OctoMesh CLI - Code Style and Conventions

## Language Features

### C# Version
- **Language Version:** Latest major C# (configured in Directory.Build.props)
- **Target Framework:** .NET 9.0
- **Nullable Reference Types:** Enabled project-wide
- **Implicit Usings:** Enabled
- **Treat Warnings as Errors:** Enforced

### Modern C# Features in Use
1. **Primary Constructors** - Used extensively for base classes and commands
   ```csharp
   internal abstract class ServiceClientOctoCommand<TServiceClientType>(
       ILogger<ServiceClientOctoCommand<TServiceClientType>> logger,
       string commandGroup,
       string commandValue,
       string commandDescription,
       IOptions<OctoToolOptions> options,
       TServiceClientType serviceClient,
       IAuthenticationService authenticationService)
       : Command<OctoToolOptions>(logger, commandGroup, commandValue, commandDescription, options)
   ```

2. **File-Scoped Namespaces** - Used throughout the codebase
   ```csharp
   namespace Meshmakers.Octo.Frontend.ManagementTool;
   
   internal static class Program
   {
       // ...
   }
   ```

3. **Collection Expressions** - Modern array/collection initialization
   ```csharp
   _eMailArg = CommandArgumentValue.AddArgument("e", "eMail", ["E-Mail of user"], true, 1);
   ```

4. **Pattern Matching** - Used for type checking and null handling

## Naming Conventions

### Classes and Types
- **PascalCase** for class names: `CreateUser`, `ServiceClientOctoCommand`, `AuthenticationService`
- **Internal** access modifier for implementation classes (not public libraries)
- **Abstract** base classes for shared functionality

### Interfaces
- **I prefix + PascalCase**: `ICommand`, `IAuthenticationService`, `IServiceClient`
- Interfaces define contracts for dependency injection

### Methods and Properties
- **PascalCase** for public/protected methods: `Execute()`, `PreValidate()`, `EnsureAuthenticated()`
- **PascalCase** for public/protected properties: `ServiceClient`, `Options`, `Logger`

### Fields
- **_camelCase** (underscore prefix) for private fields: `_logger`, `_parser`, `_eMailArg`, `_nameArg`
- **Readonly** where applicable

### Parameters and Local Variables
- **camelCase** for parameters: `logger`, `options`, `commandGroup`, `serviceClient`
- **camelCase** for local variables: `eMail`, `name`, `password`, `userDto`

### Constants
- **PascalCase** for constants (when used in Constants.cs): `OctoExeName`, `IdentityServicesGroup`

## Code Organization

### Project Structure
- Commands organized by **service domain** in separate folders:
  - `Commands/Implementations/Asset/`
  - `Commands/Implementations/Identity/`
  - `Commands/Implementations/Bots/`
  - etc.

### Class Organization (Top to Bottom)
1. Private fields
2. Constructor
3. Public methods (Execute, PreValidate, etc.)
4. Protected methods
5. Private methods
6. Helper/utility methods

### Dependency Injection
- **Constructor injection** is the standard pattern
- Services registered in `Program.cs` using `IServiceCollection`
- Options pattern used for configuration (`IOptions<T>`)
- Primary constructors used for concise constructor definitions

## Command Pattern Implementation

### Base Classes
All commands inherit from one of these base classes:

1. **ServiceClientOctoCommand<T>** - For commands that interact with OctoMesh services
   - Handles authentication automatically in `PreValidate()`
   - Provides access to `ServiceClient` and `Options`

2. **JobOctoCommand** - For commands that initiate long-running jobs
   - Includes job status polling in `WaitForJob()`
   - Handles job result downloads

3. **JobWithWaitOctoCommand** - For jobs that wait for completion

### Command Structure
```csharp
internal class CreateUser : ServiceClientOctoCommand<IIdentityServicesClient>
{
    // 1. Private fields for command arguments
    private readonly IArgument _eMailArg;
    private readonly IArgument _nameArg;
    
    // 2. Constructor - register arguments and set command metadata
    public CreateUser(ILogger<CreateUser> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "CreateUser", "Create a new user account", options,
            identityServicesClient, authenticationService)
    {
        _eMailArg = CommandArgumentValue.AddArgument("e", "eMail", ["E-Mail of user"], true, 1);
        _nameArg = CommandArgumentValue.AddArgument("un", "userName", ["User name"], true, 1);
    }
    
    // 3. Execute method - main command logic
    public override async Task Execute()
    {
        // Get argument values
        var eMail = CommandArgumentValue.GetArgumentScalarValue<string>(_eMailArg).ToLower();
        
        // Log operations
        Logger.LogInformation("Creating user '{Name}'", name);
        
        // Call service
        await ServiceClient.CreateUser(userDto);
        
        // Log completion
        Logger.LogInformation("User '{Name}' added", name);
    }
}
```

## Logging Conventions

### Structured Logging
- Use **Microsoft.Extensions.Logging** with NLog provider
- Use **structured logging** with placeholders:
  ```csharp
  Logger.LogInformation("Creating user '{Name}' at '{ServiceUri}'", name, ServiceClient.ServiceUri);
  ```

### Log Levels
- `LogInformation` - Normal operations, user-visible actions
- `LogError` - Errors with context
- `LogCritical` - Critical failures

### Log Message Format
- Use single quotes around variable values in messages: `'{Name}'`, `'{ServiceUri}'`
- Use descriptive placeholder names matching the variable

## Error Handling

### Exception Handling
- Custom exception types: `ToolException`, `ServiceConfigurationMissingException`, etc.
- Specific error codes in `Runner.cs`:
  - `-1` - Parameter/argument errors
  - `-2` - Service configuration missing
  - `-3` - Service client errors
  - `-4` - Authentication/authorization failures
  - `-5` - Tool-specific errors
  - `-99` - Unhandled exceptions

### Exception Throwing
```csharp
throw ToolException.JobFailed(id, jobDto.StateChangedAt?.ToLocalTime(), jobDto.ErrorMessage);
```

## Async/Await Patterns

### Always Use Async
- All I/O operations are async: `Execute()`, `PreValidate()`, `WaitForJob()`
- Return `Task` or `Task<T>` for async methods
- Use `await` for async calls (no `.Result` or `.Wait()`)

### Async Method Naming
- **Use `Async` suffix** for async methods in service clients: `CreateUserAsync()`, `GetJobStatusAsync()`
- Command methods like `Execute()` don't use the suffix (framework convention)

## Documentation

### XML Documentation
- **Not heavily used** in this codebase
- Command descriptions provided inline in constructor calls
- Clear, self-documenting code preferred

### Comments
- Use comments sparingly for complex logic
- Prefer clear naming over explanatory comments

## Configuration and Options

### Options Pattern
```csharp
services.Configure<OctoToolOptions>(options =>
    config.GetSection(Constants.OctoToolOptionsRootNode).Bind(options));
```

### Accessing Options
```csharp
IOptions<OctoToolOptions> options  // Injected
Options.Value.TenantId            // Accessed in commands
```

## Testing Conventions

**Note:** This solution does not include test projects. Testing is performed at the service level in other repositories.

## Assembly Attributes

### Project-Level Settings (Directory.Build.props)
- `LangVersion` - latestmajor
- `Nullable` - enable
- `TreatWarningsAsErrors` - true
- `ImplicitUsings` - true
- `TargetFramework` - net9.0

## File Organization

### Namespace Structure
- Root: `Meshmakers.Octo.Frontend.ManagementTool`
- Commands: `Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.<Domain>`
- Services: `Meshmakers.Octo.Frontend.ManagementTool.Services`

### File Naming
- **One class per file**
- **File name matches class name**: `CreateUser.cs` contains `CreateUser` class
