# OctoMesh CLI - Project Overview

## Purpose
OctoMesh CLI (`octo-cli`) is a command-line management tool for the OctoMesh data mesh platform. It provides administrative commands for managing tenants, identity services, bots, communication services, reporting, and asset repositories across the OctoMesh ecosystem.

## Repository Structure

```
octo-cli/
├── src/
│   ├── ManagementTool/          # Main CLI application (compiles to octo-cli.exe)
│   │   ├── Commands/
│   │   │   ├── Implementations/
│   │   │   │   ├── Asset/       # Tenant, model, time series commands
│   │   │   │   ├── Bots/        # Service hooks, notifications
│   │   │   │   ├── Communication/ # Enable/disable communication
│   │   │   │   ├── DevOps/      # Certificate generation
│   │   │   │   ├── Diagnostics/ # Log level configuration
│   │   │   │   ├── General/     # Authentication commands
│   │   │   │   ├── Identity/    # Users, roles, clients, API resources
│   │   │   │   └── Reporting/   # Enable/disable reporting
│   │   │   ├── ServiceClientOctoCommand.cs  # Base class for service commands
│   │   │   ├── JobOctoCommand.cs           # Base for job-based commands
│   │   │   └── JobWithWaitOctoCommand.cs   # Jobs with wait functionality
│   │   ├── Services/
│   │   │   ├── AuthenticationService.cs
│   │   │   └── IAuthenticationService.cs
│   │   ├── Program.cs          # Application entry point
│   │   └── Runner.cs           # Command execution orchestrator
│   └── GraphQlDtos/            # Generated GraphQL DTOs from construction kit models
├── devops-build/               # Azure DevOps pipeline definitions
├── assets/                     # Icons and branding assets
├── Directory.Build.props       # MSBuild properties and configuration
└── Octo.Cli.sln               # Solution file

```

## Tech Stack

### Framework & Language
- **.NET 9.0** (net9.0) - Target framework
- **C# (latest major)** - Language version with modern features
- **Nullable reference types** - Enabled project-wide
- **Implicit usings** - Enabled
- **Treat warnings as errors** - Enforced for code quality

### Key Dependencies
- **Meshmakers.Common.CommandLineParser** - Command-line parsing infrastructure
- **Meshmakers.Common.Configuration** - Configuration management with user profile support
- **Meshmakers.Octo.Sdk.ServiceClient** - Client SDKs for all OctoMesh services
- **System.IdentityModel.Tokens.Jwt** - JWT token handling for authentication
- **NLog.Extensions.Logging** - Structured logging
- **Microsoft.Extensions.DependencyInjection** - Dependency injection
- **Microsoft.Extensions.Configuration** - Configuration providers

### Service Clients
The CLI communicates with multiple OctoMesh microservices:
- **IAssetServicesClient** - Asset repository and tenant operations
- **IBotServicesClient** - Bot services and service hooks
- **IIdentityServicesClient** - Identity and access management
- **ICommunicationServicesClient** - Communication controller
- **IReportingServicesClient** - Report generation
- **IAdminPanelClient** - Admin panel operations

## Build Configurations

### DebugL (Local Development)
**IMPORTANT:** Always use `DebugL` configuration for local development!
- Package version: `999.0.0`
- Uses local NuGet feed: `../nuget`
- Configured in `Directory.Build.props`

### Debug
- Standard development build
- Standard versioning

### Release
- Production build with optimizations
- Single-file publish
- Self-contained deployment
- Platform-specific runtimes: `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`

## Configuration System

The tool uses a layered configuration approach:

1. **Application configuration**: `appsettings.json` in executable directory (optional)
2. **User profile configuration**: `~/.octo-tool/settings.json` (created by `config` command)

Configuration classes:
- `OctoToolOptions` - Service URLs and tenant ID
- `OctoToolAuthenticationOptions` - Authentication tokens and state

## Architecture Patterns

### Command Pattern
All CLI commands implement `ICommand` from `Meshmakers.Common.CommandLineParser`:
- Commands registered in `Program.cs` via dependency injection
- Parsed and validated by `ICommandParser`
- Executed through the `Runner` class

### Command Base Classes
1. **ServiceClientOctoCommand<T>** - Commands that interact with OctoMesh services (handles authentication)
2. **JobOctoCommand** - Commands that initiate long-running jobs
3. **JobWithWaitOctoCommand** - Commands that initiate jobs and wait for completion

### Authentication Flow
1. User runs `login` command with credentials
2. Tool authenticates against IdentityServer using device code flow
3. Access token cached in user configuration (`~/.octo-tool/settings.json`)
4. `ServiceClientOctoCommand` ensures authentication before command execution
5. Tokens automatically refreshed using `IAuthenticatorClient`

### GraphQL Integration
The `GraphQlDtos` project uses OctoMesh's MSBuild tasks and source generation:
- `construction-kits.yaml` defines imported system models
- `Meshmakers.Octo.ConstructionKit.MsBuildTasks` compiles models at build time
- `Meshmakers.Octo.Sdk.SourceGeneration` generates C# types via Roslyn source generators
- Generated constants in `GraphQlConstants.cs` contain GraphQL query/mutation strings

## Output Location
All builds output to: `bin/$(Configuration)/`

This centralizes build artifacts for all projects in the solution.

## NuGet Configuration

Package sources (defined in `Directory.Build.props`):
- **Local feed (DebugL only)**: `$(OctoRepoRootPath)../nuget`
- **Private server**: `$(OctoNugetPrivateServer)` (set via environment or `Octo.User.props`)
- **Public NuGet**: `https://api.nuget.org/v3/index.json`

Version strategy:
- **Private builds**: `0.1.*` (when `OctoNugetPrivateServer` is set)
- **Public builds**: `3.2.*` (when no private server configured)
- **Local development**: `999.0.0` (DebugL configuration)
