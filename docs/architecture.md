---
description: Project structure, key services, configuration files, environment variables and the command categories of octo-cli.
applies_to: src/ManagementTool/*.cs, **/Services/**, *.csproj, Octo.Cli.sln, devops-build/**
---

# Architecture and configuration

## Project Structure

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
│   │       │   └── Context/         # AddContext, RemoveContext, UseContext, ListContexts
│   │       ├── Identity/     # User, role, client, provider, group, email domain rules, external mapping, admin provisioning commands
│   │       └── Reporting/    # Report service commands
│   ├── Services/             # Service layer (AuthenticationService, ContextManager, etc.)
│   ├── Program.cs            # Entry point
│   └── Runner.cs             # Command execution orchestrator
└── GraphQlDtos/              # GraphQL DTOs and CK method clients generated from construction-kits.yaml (System, System.Bot, System.Communication, System.Identity)

tests/
├── CommandReferenceGenerator.Tests/  # Tests for the Roslyn doc generator
└── ManagementTool.Tests/             # Unit tests for the CLI service layer (xUnit)
```

Tests run via `dotnet test Octo.Cli.sln`; the CI pipeline (`azure-pipelines.yml`,
`-c Release`) discovers every test project in the solution automatically, so a new
test project only needs to be added to the solution — no pipeline change. Locally,
build/test in `DebugL` (the local NuGet feed has the current 999.0.0 service-client
packages; `Release` resolves them from the private CI feed). `ContextManager` exposes
a `ContextManager(string baseDirectory)` constructor so tests use a throwaway
directory instead of the real `~/.octo-cli`.

**A new project must opt into `DebugL` explicitly**, in two places, or the local build
breaks in a confusing way:

1. `<Configurations>Debug;Release;DebugL</Configurations>` in its `.csproj` — the SDK
   default is `Debug;Release`.
2. `DebugL|<platform>.ActiveCfg` / `.Build.0` = `DebugL|Any CPU` in `Octo.Cli.sln`
   (`dotnet sln add` writes `Debug|Any CPU` there).

Miss either one and the project restores as `Debug`, whose `RestoreSources` do not
include the local `../nuget` feed, while the projects it references still demand the
999.0.0 packages — the result is a `NU1102` that only appears once the package is no
longer in the global NuGet cache.

## Key Components

- **ContextManager** (`Services/ContextManager.cs`): Manages named contexts stored in `~/.octo-cli/contexts.json`. Each context holds its own `OctoToolOptions` (service URIs, tenant) and `OctoToolAuthenticationOptions` (tokens). Supports migration from legacy `settings.json`. **Context names are matched case-insensitively** (`StringComparer.OrdinalIgnoreCase`), so `UseContext`/`RemoveContext` accept any casing; the active context is persisted using the stored key's canonical casing. `RequestedContextName` keeps the name exactly as passed to `SelectContext`, so a command that replaces a context's login can tell a case-only match (`LogInClientCredentials`). Because System.Text.Json deserializes the `Contexts` dictionary with the ordinal comparer, `Load()` rebuilds it with `OrdinalIgnoreCase` after reading the file. Distinguishes the **active** context (persisted) from the **effective** one (`SelectContext` / `GetEffectiveContext`, chosen per invocation by `--context`), and serialises every write behind a cross-process lock — see [Per-invocation context override](authentication-and-contexts.md#per-invocation-context-override---context).

- **AuthenticationService** (`Services/AuthenticationService.cs`): Handles OAuth token management. Saves tokens to the *effective* context via `IContextManager` (`SaveEffectiveContext`), so `--context` sends the token to the context the command actually used. Supports both access tokens and refresh tokens.

- **ServiceClientOctoCommand**: Base class for commands that call Octo services. Automatically handles authentication via `IAuthenticationService`.

- **OctoToolAuthenticationOptions**: Configuration options for stored credentials (AccessToken, RefreshToken, AccessTokenExpiresAt).

## Configuration

The CLI uses:
- **User folder**: `~/.octo-cli/` (defined in `Constants.OctoToolUserFolderName`), or
  `$OCTO_CLI_HOME/.octo-cli/` when that variable is set
- **Context file**: `contexts.json` for multi-context configuration and authentication data
- **Lock file**: `contexts.lock`, held while `contexts.json` is rewritten so parallel invocations
  cannot lose each other's changes
- **Legacy file**: `settings.json` (auto-migrated to `contexts.json` on first run)
- **Context manager**: `IContextManager` for loading/saving context configuration

Environment variables are prefixed with `OCTO_`:

| Variable | Purpose |
|---|---|
| `OCTO_CLI_CONTEXT` | Context used for this invocation, like `--context` but for a whole subshell. The argument wins when both are set. |
| `OCTO_CLI_HOME` | Parent directory of the `.octo-cli` folder. Give parallel jobs different values to get fully separate `contexts.json` files. Trimmed before use — untrimmed whitespace would silently resolve to a different directory rather than fail. |
| `OCTO_CLI_CLIENT_ID` / `OCTO_CLI_CLIENT_SECRET` | Credentials for `LogInClientCredentials` and for automatic token renewal. |
| `OCTO_CLI_NEW_PASSWORD` | Password a command sets (`ResetPassword`, `CreateUser`, `Setup`). |
| `OCTO_CLI_NEW_CLIENT_SECRET` | Secret of a client a command creates (`AddClientCredentialsClient`, `AddDeviceCodeClient`). Deliberately not `OCTO_CLI_CLIENT_SECRET`, which is the secret octo-cli itself logs in with. |
| `OCTO_CLI_IDP_CLIENT_SECRET` | Client secret of an identity provider (`AddOAuthIdentityProvider`, `AddAzureEntryIdIdentityProvider`, `UpdateIdentityProvider`). |
| `OCTO_CLI_AI_ACCESS_TOKEN` / `OCTO_CLI_AI_REFRESH_TOKEN` | Anthropic tokens for `RedeemAiTicket`. |
| `OCTO_CLI_CHILD_CLIENT_SECRET` | Optional. Secret of the client's **mirror** in a child tenant, used by `Delete --disable-features` for the child token. Without it `OCTO_CLI_CLIENT_SECRET` is tried in the child, which only works while the child accepts the inherited parent secret. |

## Command Categories

| Category | Commands | Service |
|----------|----------|---------|
| Identity | users, roles, clients (+ GetClientRoles; mirror commands: GetClientMirrors, ProvisionClientInExistingTenants, ProvisionClientInTenant, UnprovisionClientFromTenant, SetClientAutoProvision, ApplyClientOverlay, CleanClientOverlays), identityProviders, groups, emailDomainGroupRules, externalTenantUserMappings, adminProvisioning, apiResources, apiScopes | Identity Services |
| Asset | tenants, models, blueprints (ListBlueprints, RefreshBlueprintCatalogs, InstallBlueprint, GetBlueprintHistory, PreviewBlueprintUpdate, UpdateBlueprint, ListBlueprintInstallations, UninstallBlueprint), timeSeries (EnableStreamData, DisableStreamData, ActivateArchive, DisableArchive, EnableArchive, RetryArchiveActivation, DeleteArchive, FreezeRollupArchive, UnfreezeRollupArchive, RewindRollupWatermark, ListRollupsForArchive, RecomputeArchive, BackfillRollup, ListRecomputeJobs, ValidateRollups, RepairRollups, GetRollupRepairStatus, AddArchiveColumn, AddComputedColumn, RemoveComputedColumn, UpdateComputedColumnFormula), operations of asynchronous CK methods (GetOperation, ListOperations) | Asset Repository |
| Bots | Dump, Restore, ExportArchiveData, ImportArchiveData, RunFixupScripts | Bot Services |
| Communication | enable/disable, adapters (incl. RotateAdapterServiceAccountSecret), SwitchServiceAccountsToImpersonation, pipelines (incl. MovePipelines for bulk reassignment to a different adapter), triggers, pools, adapter pool queue (GetAdapterPoolQueue, CancelQueuedExecution), dataFlows, workloads (GetWorkloadsByChart, UpdateWorkloadChartVersion, UpdateWorkloadResources, DeployWorkload, UndeployWorkload) | Communication Controller |
| Reporting | enable/disable | Report Services |
| AI Services | EnableAi, DisableAi, RedeemAiTicket (anonymous — bastion-side), GetAiCredentialsStatus, RevokeAiCredentials; CreateAgent, UpdateAgent, EnableAgent, DisableAgent, GetAgents, GetAgentRuns (through the MCP agent tools) | AI Services |
| DevOps | certificates | Local operations |
| General | login, loginClientCredentials, authStatus, config | Local operations |
| Context | addContext, removeContext, useContext, listContexts | Local operations |

> **Tenant-scoped service clients.** The StreamData and Reporting service clients
> are now tenant-scoped (like Communication): `Program.cs` sets `TenantId` (from the effective
> context) on `StreamDataServiceClientOptions` and `ReportingServicesClientOptions`. The SDK
> routes these to `{tenantId}/v1` and throws `ServiceConfigurationMissingException` if the
> effective context has no `TenantId`, so `EnableStreamData` / archive / rollup / computed-column
> commands and `EnableReporting` / `DisableReporting` require a context with a tenant set. The
> former system-scoped enable/disable endpoints were removed server-side.
