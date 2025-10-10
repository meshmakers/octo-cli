# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository Overview

OctoMesh CLI (`octo-cli`) is a command-line management tool for the OctoMesh data mesh platform. Built with .NET 9.0, it provides administrative commands for managing tenants, identity services, bots, communication services, reporting, and asset repositories across the OctoMesh ecosystem.

## Project Structure

The repository contains two main projects:

- **ManagementTool**: The main CLI application (compiled as `octo-cli`) containing all command implementations
- **GraphQlDtos**: Generated GraphQL DTOs from construction kit models using OctoMesh source generation

### Command Organization

Commands are organized by service domain under `src/ManagementTool/Commands/Implementations/`:

- **Asset**: Tenant management, model import/export, fixup scripts, time series operations
- **Bots**: Service hooks and notification message management
- **Communication**: Enable/disable communication services
- **DevOps**: Certificate generation for operators
- **Diagnostics**: Log level reconfiguration
- **General**: Authentication (login, auth status)
- **Identity**: Users, roles, clients, API resources/scopes/secrets, identity providers
- **Reporting**: Enable/disable reporting services

All commands inherit from base classes:
- `ServiceClientOctoCommand<T>`: Commands that interact with OctoMesh services (handles authentication)
- `JobOctoCommand`: Commands that initiate long-running jobs
- `JobWithWaitOctoCommand`: Commands that initiate jobs and wait for completion

## Development Commands

### Building

```bash
# Build with local dependencies (use this configuration for development)
dotnet build Octo.Cli.sln --configuration DebugL

# Standard development build
dotnet build Octo.Cli.sln --configuration Debug

# Production build
dotnet build Octo.Cli.sln --configuration Release
```

**IMPORTANT**: Use `DebugL` configuration for local development. This sets package versions to `999.0.0` and uses the local NuGet feed at `../nuget`.

### Running the CLI

```bash
# Run directly
dotnet run --project src/ManagementTool/ManagementTool.csproj --configuration DebugL -- [command] [args]

# Or use the built executable
./bin/DebugL/octo-cli [command] [args]
```

### Common CLI Commands

```bash
# Configure the tool with service endpoints
octo-cli config -isu https://localhost:5003/ -asu https://localhost:5001/ -tid tenant-name

# Login to OctoMesh services
octo-cli login

# Create a new tenant
octo-cli asset:tenant create -tid tenant-id -db database-name

# Import construction kit models
octo-cli asset:models import-ck -ckf path/to/model.yaml

# Manage users
octo-cli identity:users get
octo-cli identity:users create -uid user-id -pwd password

# Manage service hooks
octo-cli bots:servicehooks get
octo-cli bots:servicehooks create -n "Hook Name" -uri https://webhook.url
```

## Build Configuration

### Target Framework and Language
- **Target Framework**: .NET 9.0 (net9.0)
- **Language Version**: Latest major C#
- **Nullable Reference Types**: Enabled
- **Treat Warnings as Errors**: true

### Build Configurations

Three build configurations are supported:

1. **Debug**: Standard development build
2. **DebugL**: Local development build with:
   - Package version: `999.0.0`
   - Local NuGet feed: `../nuget`
   - OctoVersion: `999.0.0`
3. **Release**: Production build with:
   - Single-file publish
   - Self-contained deployment
   - Platform-specific runtimes: `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`

### Publishing

```bash
# Publish for specific runtime
dotnet publish src/ManagementTool/ManagementTool.csproj --configuration Release --runtime win-x64

# The build pipeline publishes for all platforms automatically
```

The CI/CD pipeline (`devops-build/octo-cli-pipeline.yml`) builds for all supported platforms and creates zip artifacts.

## Key Dependencies

### External Packages
- **Meshmakers.Common.CommandLineParser**: Command-line parsing and command infrastructure
- **Meshmakers.Common.Configuration**: Configuration management with user profile support
- **Meshmakers.Octo.Sdk.ServiceClient**: Client SDKs for all OctoMesh services
- **System.IdentityModel.Tokens.Jwt**: JWT token handling
- **NLog.Extensions.Logging**: Structured logging

### Internal Projects
- **GraphQlDtos**: Generated GraphQL types and queries from construction kit models

## Configuration System

The tool uses a layered configuration approach:

1. `appsettings.json` in the executable directory (optional)
2. `~/.octo-tool/settings.json` in the user profile directory (created by `config` command)

Configuration is managed via:
- `OctoToolOptions`: Service URLs and tenant ID
- `OctoToolAuthenticationOptions`: Authentication tokens and state

The `config` command updates the user profile settings file.

## Architecture Patterns

### Command Pattern
All CLI commands implement `ICommand` from `Meshmakers.Common.CommandLineParser`. Commands are:
- Registered in `Program.cs` via dependency injection
- Discovered and parsed by `ICommandParser`
- Executed via the `Runner` class

### Service Client Pattern
Each OctoMesh service has a dedicated client interface:
- `IAssetServicesClient`: Asset repository and tenant operations
- `IBotServicesClient`: Bot services and service hooks
- `IIdentityServicesClient`: Identity and access management
- `ICommunicationServicesClient`: Communication controller
- `IReportingServicesClient`: Report generation
- `IAdminPanelClient`: Admin panel operations

All service clients require authentication tokens managed by `IAuthenticationService`.

### Authentication Flow
1. User runs `login` command with credentials
2. Tool authenticates against IdentityServer using device code flow
3. Access token is cached in user configuration
4. `ServiceClientOctoCommand` ensures authentication before command execution
5. Access tokens are automatically refreshed using `IAuthenticatorClient`

### GraphQL Integration
The `GraphQlDtos` project uses OctoMesh's MSBuild tasks and source generation:
- `construction-kits.yaml` defines imported system models
- `Meshmakers.Octo.ConstructionKit.MsBuildTasks` compiles construction kit models at build time
- `Meshmakers.Octo.Sdk.SourceGeneration` generates C# types as Roslyn source generators
- Generated constants in `GraphQlConstants.cs` contain GraphQL query/mutation strings

## Testing

No test projects are currently included in this solution. Testing is likely performed at the service level in other repositories.

## NuGet Configuration

Package sources (defined in `Directory.Build.props`):
- **Local feed** (DebugL only): `$(OctoRepoRootPath)../nuget`
- **Private server**: `$(OctoNugetPrivateServer)` (set via environment or `Octo.User.props`)
- **Public NuGet**: `https://api.nuget.org/v3/index.json`

Version strategy:
- **Private builds**: `0.1.*` (when `OctoNugetPrivateServer` is set)
- **Public builds**: `3.2.*` (when no private server configured)
- **Local development**: `999.0.0` (DebugL configuration)

## Output Location

All builds output to:
```
bin/$(Configuration)/
```

This centralizes build artifacts for all projects in the solution.
