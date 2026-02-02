# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Octo CLI (`octo-cli`) is a .NET 10 command-line management tool for the Octo Mesh Platform. It provides commands for managing tenants, users, roles, clients, identity providers, and other platform resources across all Octo services.

## Build Commands

```bash
# Restore and build
dotnet restore Octo.Cli.sln
dotnet build Octo.Cli.sln

# Build with specific configuration
dotnet build Octo.Cli.sln -c Release
dotnet build Octo.Cli.sln -c DebugL  # Local development with version 999.0.0

# Run the CLI
dotnet run --project src/ManagementTool/ManagementTool.csproj -- <command> [options]

# Run tests
dotnet test Octo.Cli.sln
```

## Build Configurations

- **Debug/Release**: Standard configurations
- **DebugL**: Local development mode that sets version to 999.0.0 and uses local NuGet sources from `../nuget`

## Architecture

### Project Structure

```
src/
├── ManagementTool/           # Main CLI application
│   ├── Commands/             # Command implementations
│   │   └── Implementations/  # Organized by service domain
│   │       ├── Asset/        # Tenant, model, time-series commands
│   │       ├── Bots/         # Notification, service hook commands
│   │       ├── Communication/# Communication controller commands
│   │       ├── Diagnostics/  # Log level commands
│   │       ├── DevOps/       # Certificate generation commands
│   │       ├── General/      # Authentication commands
│   │       ├── Identity/     # User, role, client, provider commands
│   │       └── Reporting/    # Report service commands
│   ├── Services/             # Service layer (AuthenticationService, etc.)
│   ├── Program.cs            # Entry point
│   └── Runner.cs             # Command execution orchestrator
└── GraphQlDtos/              # GraphQL DTOs for service communication
```

### Key Components

- **AuthenticationService** (`Services/AuthenticationService.cs`): Handles OAuth token management. Supports both access tokens and refresh tokens. Works with device flow (no refresh token) and standard flows (with refresh token).

- **ServiceClientOctoCommand**: Base class for commands that call Octo services. Automatically handles authentication via `IAuthenticationService`.

- **OctoToolAuthenticationOptions**: Configuration options for stored credentials (AccessToken, RefreshToken, AccessTokenExpiresAt).

### Authentication Flow

The CLI supports multiple authentication methods:

1. **Device Code Flow** (recommended for CLI):
   - User runs `octo-cli login`
   - CLI displays device code and verification URL
   - User authenticates in browser
   - CLI receives access token (no refresh token with device flow)
   - Token stored in `~/.octo-cli/settings.json`

2. **Client Credentials Flow**:
   - For automated/service scenarios
   - Uses client ID and secret

### Token Storage

Credentials are stored in `~/.octo-cli/settings.json`:

```json
{
  "AccessToken": "eyJ...",
  "RefreshToken": "optional...",
  "AccessTokenExpiresAt": "2024-01-01T00:00:00Z"
}
```

**Note**: Device flow does not provide a refresh token (no `offline_access` scope). The `AuthenticationService` handles this gracefully by using the access token directly without attempting refresh.

## Configuration

The CLI uses:
- **User folder**: `~/.octo-cli/` (defined in `Constants.OctoToolUserFolderName`)
- **Settings file**: `settings.json` for authentication data
- **Config writer**: `IConfigWriter` for persisting settings

Environment variables are prefixed with `OCTO_`.

## Command Categories

| Category | Commands | Service |
|----------|----------|---------|
| Identity | users, roles, clients, identityProviders, apiResources, apiScopes | Identity Services |
| Asset | tenants, models, timeSeries | Asset Repository |
| Bots | notifications, serviceHooks | Bot Services |
| Communication | enable/disable | Communication Controller |
| Reporting | enable/disable | Report Services |
| DevOps | certificates | Local operations |
| General | login, authStatus, config | Local operations |

## Common Operations

```bash
# Login via device code flow
octo-cli login

# Check authentication status
octo-cli authStatus

# List identity providers
octo-cli identityProviders get

# Add Azure Entra ID provider
octo-cli identityProviders addAzureEntraId -n "Azure AD" -c <clientId> -s <secret> -t <tenantId>

# List users
octo-cli users get

# Create tenant
octo-cli tenants create -n "MyTenant"
```

## Documentation Guidelines

**CRITICAL REQUIREMENT:** Documentation MUST be updated after EVERY change. This is mandatory, not optional.

### Language Requirement

All documentation MUST be written in **English**. This includes:
- README.md files
- Code comments
- API documentation
- This CLAUDE.md file

### Mandatory Documentation Updates

After making ANY code changes, you MUST update:

1. **For New Commands**: Document command usage, parameters, and examples
2. **For Bug Fixes**: Update troubleshooting sections if applicable
3. **For Authentication Changes**: Update the Authentication Flow section above
4. **For Configuration Changes**: Update the Configuration section

### Key Files to Update

| File | When to Update |
|------|----------------|
| `README.md` | Project overview, usage examples |
| `CLAUDE.md` | Project structure, architecture changes |
| Command help text | When command parameters change |
