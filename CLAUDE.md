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
│   │       ├── General/      # Authentication and context commands
│   │       │   ├── Authentication/  # LogIn, AuthStatus
│   │       │   └── Context/         # AddContext, RemoveContext, UseContext
│   │       ├── Identity/     # User, role, client, provider, group, email domain rules, external mapping, admin provisioning commands
│   │       └── Reporting/    # Report service commands
│   ├── Services/             # Service layer (AuthenticationService, ContextManager, etc.)
│   ├── Program.cs            # Entry point
│   └── Runner.cs             # Command execution orchestrator
└── GraphQlDtos/              # GraphQL DTOs for service communication
```

### Key Components

- **ContextManager** (`Services/ContextManager.cs`): Manages named contexts stored in `~/.octo-cli/contexts.json`. Each context holds its own `OctoToolOptions` (service URIs, tenant) and `OctoToolAuthenticationOptions` (tokens). Supports migration from legacy `settings.json`.

- **AuthenticationService** (`Services/AuthenticationService.cs`): Handles OAuth token management. Saves tokens to the active context via `IContextManager`. Supports both access tokens and refresh tokens.

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

### Token Storage & Context Management

The CLI uses named contexts (similar to `kubectl config use-context`) stored in `~/.octo-cli/contexts.json`. Each context holds service URIs, tenant ID, and authentication tokens independently:

```json
{
  "ActiveContext": "dev",
  "Contexts": {
    "dev": {
      "OctoToolOptions": {
        "IdentityServiceUrl": "https://localhost:5003/",
        "AssetServiceUrl": "https://localhost:5001/",
        "TenantId": "octosystem"
      },
      "Authentication": {
        "AccessToken": "eyJ...",
        "RefreshToken": "...",
        "AccessTokenExpiresAt": "2024-01-01T00:00:00Z"
      }
    },
    "prod": { "..." : "..." }
  }
}
```

**Migration**: On first run, if `contexts.json` does not exist but `settings.json` does, the CLI automatically imports the legacy settings as a `"default"` context. The original `settings.json` is kept intact.

**Note**: The device code flow already requests `offline_access` scope, so refresh tokens are returned and persisted per context.

## Configuration

The CLI uses:
- **User folder**: `~/.octo-cli/` (defined in `Constants.OctoToolUserFolderName`)
- **Context file**: `contexts.json` for multi-context configuration and authentication data
- **Legacy file**: `settings.json` (auto-migrated to `contexts.json` on first run)
- **Context manager**: `IContextManager` for loading/saving context configuration

Environment variables are prefixed with `OCTO_`.

## Command Categories

| Category | Commands | Service |
|----------|----------|---------|
| Identity | users, roles, clients, identityProviders, groups, emailDomainGroupRules, externalTenantUserMappings, adminProvisioning, apiResources, apiScopes | Identity Services |
| Asset | tenants, models, timeSeries | Asset Repository |
| Bots | notifications, serviceHooks | Bot Services |
| Communication | enable/disable | Communication Controller |
| Reporting | enable/disable | Report Services |
| DevOps | certificates | Local operations |
| General | login, authStatus, config | Local operations |
| Context | addContext, removeContext, useContext | Local operations |

## Common Operations

```bash
# Context management
octo-cli -c AddContext -n dev -isu https://localhost:5003/ -tid octosystem
octo-cli -c AddContext -n prod -isu https://id.example.com/ -tid customer1
octo-cli -c UseContext -n dev       # Switch to dev context
octo-cli -c UseContext              # List all contexts (no -n arg)
octo-cli -c RemoveContext -n prod   # Remove a context

# Login via device code flow (tokens saved to active context)
octo-cli -c LogIn

# Check authentication status
octo-cli -c AuthStatus

# Configure the active context
octo-cli -c Config -isu https://localhost:5003/ -asu https://localhost:5001/ -tid meshtest

# List identity providers
octo-cli identityProviders get

# Add Azure Entra ID provider
octo-cli identityProviders addAzureEntraId -n "Azure AD" -c <clientId> -s <secret> -t <tenantId>

# List users
octo-cli users get

# Create tenant
octo-cli tenants create -n "MyTenant"

# Groups management
octo-cli -c GetGroups
octo-cli -c CreateGroup -n "MyGroup" -d "Description" -rids "role1,role2"
octo-cli -c UpdateGroup -id <groupId> -n "NewName"
octo-cli -c DeleteGroup -id <groupId>
octo-cli -c UpdateGroupRoles -id <groupId> -rids "role1,role2"
octo-cli -c AddUserToGroup -id <groupId> -uid <userId>
octo-cli -c RemoveUserFromGroup -id <groupId> -uid <userId>
octo-cli -c AddGroupToGroup -id <parentGroupId> -cgid <childGroupId>
octo-cli -c RemoveGroupFromGroup -id <parentGroupId> -cgid <childGroupId>

# Email domain group rules
octo-cli -c GetEmailDomainGroupRules
octo-cli -c CreateEmailDomainGroupRule -edp "meshmakers.com" -tgid <groupRtId>
octo-cli -c UpdateEmailDomainGroupRule -id <ruleId> -edp "meshmakers.com" -tgid <groupRtId>
octo-cli -c DeleteEmailDomainGroupRule -id <ruleId>

# External tenant user mappings
octo-cli -c GetExternalTenantUserMappings -stid <sourceTenantId>
octo-cli -c CreateExternalTenantUserMapping -stid <sourceTenantId> -suid <sourceUserId> -sun <sourceUserName>
octo-cli -c UpdateExternalTenantUserMapping -id <mappingId> -rids "role1,role2"
octo-cli -c DeleteExternalTenantUserMapping -id <mappingId>

# Admin provisioning (run from system tenant context)
octo-cli -c GetAdminProvisioningMappings -ttid <targetTenantId>
octo-cli -c CreateAdminProvisioningMapping -ttid <targetTenantId> -stid <sourceTenantId> -suid <sourceUserId> -sun <sourceUserName>
octo-cli -c ProvisionCurrentUser -ttid <targetTenantId>
octo-cli -c DeleteAdminProvisioningMapping -ttid <targetTenantId> -mid <mappingId>

# OctoTenant identity provider (cross-tenant auth)
octo-cli -c AddOctoTenantIdentityProvider -n "ParentTenant" -e true -ptid <parentTenantId>

# Identity providers with self-registration and default group
octo-cli -c AddAzureEntryIdIdentityProvider -n "Azure" -e true -t <tenantId> -cid <clientId> -cs <secret> -asr false -dgid <groupRtId>
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
