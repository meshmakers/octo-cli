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
│   │       │   └── Context/         # AddContext, RemoveContext, UseContext, ListContexts
│   │       ├── Identity/     # User, role, client, provider, group, email domain rules, external mapping, admin provisioning commands
│   │       └── Reporting/    # Report service commands
│   ├── Services/             # Service layer (AuthenticationService, ContextManager, etc.)
│   ├── Program.cs            # Entry point
│   └── Runner.cs             # Command execution orchestrator
└── GraphQlDtos/              # GraphQL DTOs for service communication

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

### Key Components

- **ContextManager** (`Services/ContextManager.cs`): Manages named contexts stored in `~/.octo-cli/contexts.json`. Each context holds its own `OctoToolOptions` (service URIs, tenant) and `OctoToolAuthenticationOptions` (tokens). Supports migration from legacy `settings.json`. **Context names are matched case-insensitively** (`StringComparer.OrdinalIgnoreCase`), so `UseContext`/`RemoveContext` accept any casing; the active context is persisted using the stored key's canonical casing. Because System.Text.Json deserializes the `Contexts` dictionary with the ordinal comparer, `Load()` rebuilds it with `OrdinalIgnoreCase` after reading the file. Distinguishes the **active** context (persisted) from the **effective** one (`SelectContext` / `GetEffectiveContext`, chosen per invocation by `--context`), and serialises every write behind a cross-process lock — see [Per-invocation context override](#per-invocation-context-override---context).

- **AuthenticationService** (`Services/AuthenticationService.cs`): Handles OAuth token management. Saves tokens to the *effective* context via `IContextManager` (`SaveEffectiveContext`), so `--context` sends the token to the context the command actually used. Supports both access tokens and refresh tokens.

- **ServiceClientOctoCommand**: Base class for commands that call Octo services. Automatically handles authentication via `IAuthenticationService`.

- **OctoToolAuthenticationOptions**: Configuration options for stored credentials (AccessToken, RefreshToken, AccessTokenExpiresAt).

### Authentication Flow

The CLI supports multiple authentication methods:

1. **Device Code Flow** (recommended for interactive CLI use):
   - User runs `octo-cli -c LogIn -i`
   - CLI displays device code and verification URL
   - User authenticates in browser
   - CLI receives access token plus a refresh token (device flow requests `offline_access`)
   - Token stored in the effective context's `Authentication` block in `~/.octo-cli/contexts.json`
     (the active context, or the one named by `--context`)
   - **`--if-needed` (`-in`)**: non-disruptive variant. Before starting the device
     flow, the command checks whether the stored access token is still valid; if not,
     it tries a silent refresh-token exchange. The device flow is only started when
     both fail. Combine with `-i` so the browser opens automatically in that fallback
     case. Ideal for setup scripts that re-run often (e.g. tenant init) — the user is
     only prompted when the refresh token is missing or expired.

2. **Client Credentials Flow** (non-interactive — pipelines, cron jobs, headless servers, container entrypoints, batch scripts, bots, etc.):
   - Operator creates a per-tenant client once: `octo-cli -c AddClientCredentialsClient -id <id> -n "<name>" -s <secret>`
   - Caller exports `OCTO_CLI_CLIENT_ID` and `OCTO_CLI_CLIENT_SECRET` (or passes `-id`/`-s`), then runs `octo-cli -c LogInClientCredentials`
   - Tenant comes from the effective context (no `-tid` arg on the login command). Switch contexts with `UseContext`, or target another tenant for one invocation with `--context <name>`.
   - No refresh token is issued. While the env vars remain set, `EnsureAuthenticated` automatically re-acquires the token when it expires; otherwise re-run `LogInClientCredentials`.
   - Token stored in the effective context's `Authentication` block, same path as device flow.

### Token Storage & Context Management

The CLI uses named contexts (similar to `kubectl config use-context`) stored in `~/.octo-cli/contexts.json`. Each context holds service URIs, tenant ID, and authentication tokens independently. Context names are matched case-insensitively, so `UseContext -n Local_OctoSystem` resolves to a stored `local_octosystem` context:

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

### Per-invocation context override (`--context`)

`UseContext` switches the active context persistently, which makes it useless for running two
commands against different environments at the same time. **`--context <name>` selects a stored
context for a single invocation and never touches `ActiveContext`**, so parallel invocations can
each target their own tenant/environment:

```bash
octo-cli --context prod_acme -c GetUsers      # runs against prod_acme
octo-cli -c GetAdapters --context prod_acme   # position does not matter
OCTO_CLI_CONTEXT=prod_acme octo-cli -c GetPools
```

Precedence: `--context` > `OCTO_CLI_CONTEXT` > `ActiveContext`. An unknown name aborts with exit
code `-5` and lists the known contexts — there is no silent fallback to the active context.

The natural way to create the contexts is `Register-OctoCliContext -NoSwitch` in `octo-tools`
(`modules/Register-OctoCliContext.psm1`): it registers a context without switching to it, which is
exactly what `--context` then addresses.

Behaviour worth knowing:

- **It is a top-level argument, not a command argument.** It is declared by `OctoCommandParser`
  (`src/ManagementTool/OctoCommandParser.cs`) on the parser's top layer, so it works before or
  after `-c <command>` and applies to *every* command. Accepted spellings are `--context`,
  `-context` and `/context`; **no short form** exists, deliberately, so the term cannot collide
  with an argument a future command declares. `CommandReferenceGenerator` only reads command
  classes, so this argument does not appear on any generated command-reference page — it is
  visible in `octo-cli -h all`.
- **It resolves before the DI container is built.** `Program.BuildDi` fills `OctoToolOptions` and
  every service-client options object from the context, and the service clients are constructed
  while the command list is enumerated — all of that happens *before* the command line is parsed.
  `ContextSelection.Resolve` therefore pre-scans the raw arguments; the parser declaration exists
  for validation, help and a consistency check. When changing either side, keep
  `ContextOverrideResolver.IsContextTerm` in step with `Argument.Compare` in `mm-common`.
- **Tokens are written to the effective context.** `AuthenticationService.SaveAuthenticationData`
  uses `GetEffectiveContext()`, so `--context prod -c LogIn` logs into `prod` without activating
  it, and a refreshed token never lands in the wrong context.
- **`Config` refuses to run with `--context`** (exit `-5`): the override picks the context a
  command *acts on*, and rewriting a stored context definition through it is never the intent. Use
  `AddContext -n <name>` to write a named context. `UseContext` still switches the active context
  and logs a warning that `--context` does not apply to it.
- **`ListContexts` marks both**: `*` is the active context, `>` the one this invocation uses. The
  JSON output carries `isActive` and `isEffective`.

**`IContextManager` has two notions of "current"**: `GetActiveContext()` is the persisted
selection — use it only where that selection is itself the subject (`ListContexts`, `UseContext`).
Everything acting on "the context in use" wants `GetEffectiveContext()` / `SaveEffectiveContext()`.

### Parallel invocations and `contexts.json`

Because several processes can now write `contexts.json` concurrently (token refresh, `AddContext`),
every write in `ContextManager` goes through `Mutate`, which **takes a cross-process lock
(`contexts.lock`, `FileShare.None` with retries), re-reads the file, applies only its own change,
and replaces the file atomically** (temp file + `File.Move(overwrite: true)`). A reader therefore
never sees a half-written file, and two processes refreshing tokens for different contexts do not
overwrite each other. A plain "serialise my whole in-memory state" write — what the code did
before — loses one of them.

`Mutate` takes an `Action<ContextConfiguration>`, not a function returning one: a mutation can
adjust the configuration it is handed but cannot hand back a different instance, because returning
a wholesale replacement is precisely how a caller would discard a parallel invocation's changes
unnoticed. The single exception to the re-read is `MigrateIfNeeded`, which writes a whole
configuration — correct there, since it only runs when `contexts.json` does not exist yet.

For complete isolation (CI matrix jobs, throwaway environments), point each job at its own
configuration directory with **`OCTO_CLI_HOME`**: it names the *parent* of the `.octo-cli` folder,
so there is no shared file to contend for at all. Unset, the user profile is used as before. The
path in effect is logged on every run and shown by `ListContexts`.

**Note**: Device flow requests `offline_access` scope and receives a refresh token. The `AuthenticationService` handles both cases: if a refresh token is present, it refreshes expired access tokens automatically; if not, it uses the existing access token directly.

**Refresh decision & failure handling (AB#4754).** `EnsureAuthenticated` decides whether to refresh a device/interactive token from the stored `AccessTokenExpiresAt` (with a 30s margin), **not** from a live `/connect/userinfo` probe. A token that is not expiring soon is used as-is — no per-command round-trip to the identity service, and a transient identity-service hiccup no longer forces an unnecessary refresh. Server-side revocation of a not-yet-expired token still surfaces as the target service's own 401. When the refresh itself fails because the **refresh token is expired/revoked** — the typical outcome for a context that has not been used in a while, so it bites hardest when switching between contexts — the tool does one of two things: if `OCTO_CLI_CLIENT_ID` / `OCTO_CLI_CLIENT_SECRET` are set it re-acquires non-interactively via client_credentials; otherwise it fails with an actionable `ToolException` (exit `-5`) naming the effective context and the exact `octo-cli --context <name> -c LogIn` to run, instead of the raw OIDC `invalid_grant`. `AuthStatus` reports the same dead-refresh-token case as an actionable warning rather than letting it bubble up.

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

## Command Categories

| Category | Commands | Service |
|----------|----------|---------|
| Identity | users, roles, clients (+ mirror commands: GetClientMirrors, ProvisionClientInExistingTenants, ProvisionClientInTenant, UnprovisionClientFromTenant, SetClientAutoProvision, ApplyClientOverlay, CleanClientOverlays), identityProviders, groups, emailDomainGroupRules, externalTenantUserMappings, adminProvisioning, apiResources, apiScopes | Identity Services |
| Asset | tenants, models, blueprints (ListBlueprints, RefreshBlueprintCatalogs, InstallBlueprint, GetBlueprintHistory, PreviewBlueprintUpdate, UpdateBlueprint, ListBlueprintInstallations, UninstallBlueprint), timeSeries (EnableStreamData, DisableStreamData, ActivateArchive, DisableArchive, EnableArchive, RetryArchiveActivation, DeleteArchive, FreezeRollupArchive, UnfreezeRollupArchive, RewindRollupWatermark, ListRollupsForArchive, RecomputeArchive, BackfillRollup, ListRecomputeJobs, AddComputedColumn, RemoveComputedColumn, UpdateComputedColumnFormula) | Asset Repository |
| Bots | Dump, Restore, ExportArchiveData, ImportArchiveData, RunFixupScripts | Bot Services |
| Communication | enable/disable, adapters (incl. RotateAdapterServiceAccountSecret), pipelines (incl. MovePipelines for bulk reassignment to a different adapter), triggers, pools, dataFlows, workloads (GetWorkloadsByChart, UpdateWorkloadChartVersion, DeployWorkload, UndeployWorkload) | Communication Controller |
| Reporting | enable/disable | Report Services |
| AI Services | EnableAi, DisableAi, RedeemAiTicket (anonymous — bastion-side), GetAiCredentialsStatus, RevokeAiCredentials | AI Services |
| DevOps | certificates | Local operations |
| General | login, loginClientCredentials, authStatus, config | Local operations |
| Context | addContext, removeContext, useContext, listContexts | Local operations |

> **AB#4287 — tenant-scoped service clients.** The StreamData and Reporting service clients
> are now tenant-scoped (like Communication): `Program.cs` sets `TenantId` (from the active
> context) on `StreamDataServiceClientOptions` and `ReportingServicesClientOptions`. The SDK
> routes these to `{tenantId}/v1` and throws `ServiceConfigurationMissingException` if the
> effective context has no `TenantId`, so `EnableStreamData` / archive / rollup / computed-column
> commands and `EnableReporting` / `DisableReporting` require a context with a tenant set. The
> former system-scoped enable/disable endpoints were removed server-side.

## Help Options

The help flag is served by `CommandParser` / `ParserService` in
`Meshmakers.Common.CommandLineParser` (repo `mm-common`), not by octo-cli itself. It is
implicit: no command declares it, every command understands it.

> **Top-level arguments.** Besides `-c`/`--command` and the help flag, octo-cli declares one
> argument on the parser's top layer: `--context` (see
> [Per-invocation context override](#per-invocation-context-override---context)). It is understood
> by every command, in any position, and is listed in the full usage output. Commands themselves
> keep declaring their arguments via `CommandArgumentValue.AddArgument`; a top-level argument is
> added in `OctoCommandParser`, and only for something that genuinely applies to all commands.

The help has three levels, so nobody has to page through all 176 commands to find one:

```bash
# 1. Which groups are there? (~19 lines)
octo-cli -h

# 2. Which commands are in a group? Verb + description, no arguments (~94 lines for the largest group)
octo-cli -h "Identity Services"
octo-cli -h identity          # prefix match, case-insensitive
octo-cli -h Identity Services # quoting is optional, the words are joined
octo-cli -h General

# 3. What does one command take? Arguments, samples and notes (~15 lines)
octo-cli -c Create --help
octo-cli -h Create            # a topic that names a command goes straight to its help

# Everything at once, as before (~1210 lines)
octo-cli -h all
octo-cli                      # same dump on the error path: the mandatory -c is missing
```

| Input | Output | Exit |
|---|---|---|
| `-h` | group overview | 0 |
| `-h <group>` | commands of that group | 0 |
| `-h <command>` / `-c <command> -h` | help of that command | 0 |
| `-h all` | full usage listing | 0 |
| `-h <unknown>` | error naming the known groups | -1 |
| *(no arguments)* | full usage listing after the missing-argument error | -1 |

Accepted terms are `--help`, `-help`, `/help`, `-?`, `/?` and `-h` (case-insensitive). A
topic is resolved in this order: reserved `all` → exact group name → exact command name →
unique group prefix → unique group substring. Ambiguous shortenings resolve to nothing and
are reported, so `-h e` fails rather than guessing. Fuzzy matching applies to groups only —
`-h Crea` is an error, not `Create`.

**`-h` is claimed by the command first.** Declared arguments are matched before the help
flag, so a command owning `-h` keeps it — `octo-cli -c AddAdIdentityProvider -h myhost`
still binds `--host`. Use `--help` or `-?` for those commands. Give a new `AddArgument`
the short term `h` only when the command genuinely needs it; it costs the command its
`-h` shortcut for help.

Behaviour worth knowing:

- A help request suppresses validation of mandatory arguments and argument values, so
  `-c Create --help` prints help instead of complaining about the missing `-tid`.
- Help exits with code `0` and executes nothing — safe in scripts.
- Position does not matter: `--help -c Create` behaves like `-c Create --help`.
- Without help, everything is unchanged: missing mandatory arguments still fail with
  exit code `-1` followed by the full usage.
- The `NOTES` section comes from `GetDocumentation()`'s `Notes`; `SAMPLES` is filtered to
  the samples of that one command.

**Groups come from the `base(...)` call**, not from a registry: the first argument of the
`Command<T>` constructor, in practice one of the `Constants.*Group` values
(`Constants.cs:13-21`). A command using the constructor overload without a group lands in
`General`. A new group therefore exists as soon as one command names it — nothing else to
register, and it shows up in `-h` automatically.

Two names are worth avoiding for a new group: `all` is reserved for the full listing and
would make the group unreachable, and a group named exactly like a command loses nothing
but renders an extra pointer line (the group wins the name because a command is still
reachable via `-c <name> --help`, whereas a group has no second way in).

## Common Operations

```bash
# Context management
octo-cli -c AddContext -n dev -isu https://localhost:5003/ -tid octosystem
octo-cli -c AddContext -n prod -isu https://id.example.com/ -tid customer1
octo-cli -c UseContext -n dev       # Switch to dev context
octo-cli -c UseContext              # List all contexts (no -n arg, legacy shortcut)
octo-cli -c RemoveContext -n prod   # Remove a context
octo-cli -c ListContexts            # Tabular list of all contexts with auth status
octo-cli -c ListContexts -n dev     # Detail view of one context (all service URIs)
octo-cli -c ListContexts -j         # JSON output for scripting (tokens never included)

# Run a single command against another context without switching the active one.
# Works with every command and in any position.
octo-cli --context prod -c GetUsers
octo-cli -c GetAdapters --context prod
export OCTO_CLI_CONTEXT=prod        # same, for a whole subshell
# Parallel jobs against different environments (this is what the override is for):
foreach ($c in 'test_a','test_b') { Start-Job { octo-cli --context $using:c -c DeployTriggers } }
# Fully isolated configuration per job — no shared contexts.json at all:
OCTO_CLI_HOME=/tmp/job-$JOB_ID octo-cli -c AddContext -n ci -isu … -tid …

# Login via device code flow (tokens saved to the context in use)
octo-cli -c LogIn
octo-cli --context prod -c LogIn    # log into prod without making it active

# Non-disruptive login: reuse the valid token or refresh it silently, and only
# open a browser if neither works (great for re-runnable setup scripts).
octo-cli -c LogIn -i --if-needed

# Non-interactive login (pipelines, cron jobs, headless scripts, etc.)
# Tenant comes from the active context.
export OCTO_CLI_CLIENT_ID=my-client-id
export OCTO_CLI_CLIENT_SECRET=***
octo-cli -c LogInClientCredentials
# Subsequent commands auto-renew the token while the env vars remain exported.

# Check authentication status
octo-cli -c AuthStatus

# Configure the active context (rejected when combined with --context)
octo-cli -c Config -isu https://localhost:5003/ -asu https://localhost:5001/ -tid meshtest

# List identity providers
octo-cli identityProviders get

# Add Azure Entra ID provider
octo-cli identityProviders addAzureEntraId -n "Azure AD" -c <clientId> -s <secret> -t <tenantId>

# List users
octo-cli users get

# Create tenant (automatically provisions current user as admin)
octo-cli tenants create -tid mytenant -db mytenant

# Create tenant without admin provisioning
octo-cli tenants create -tid mytenant -db mytenant --no-provision

# Inspect a tenant's durable provisioning lifecycle (Creating/Active/Deleting/Failed) — AB#4348
octo-cli -c GetTenantLifecycle -tid mytenant
# Operator safety valve: re-open a stuck/Failed tenant's setup so the background reconciler finishes it
octo-cli -c ReRunTenantSetup -tid mytenant

# Blueprints (asset repository) — initial install path is Phase 1
octo-cli -c ListBlueprints                                          # list catalog blueprints across all sources
octo-cli -c RefreshBlueprintCatalogs                                # force-refresh all blueprint catalog caches at the asset repo (AB#4309)
octo-cli -c RefreshBlueprintCatalogs -cn PrivateGitHubBlueprintCatalog  # force-refresh a single catalog (case-insensitive name)
octo-cli -c InstallBlueprint -b MyBlueprint-1.0.0                   # apply blueprint to the active tenant
octo-cli -c InstallBlueprint -b MyBlueprint-1.0.0 -f                # re-apply seed data via upsert (recovery)
octo-cli -c GetBlueprintHistory                                     # show application history for the active tenant

# Blueprints — multi-install + uninstall (Phase 3)
octo-cli -c ListBlueprintInstallations                              # list blueprints currently installed on the tenant
octo-cli -c UninstallBlueprint -n MyBlueprint                       # uninstall a blueprint (locked owned entities erased)
octo-cli -c UninstallBlueprint -n MyBlueprint -c                    # cascade: also remove dependents and orphan deps
octo-cli -c UninstallBlueprint -n MyBlueprint -y                    # skip the confirmation prompt

# Blueprints — update (Phase 2a: operations layer; richer diff/merge in Phase 2b)
octo-cli -c PreviewBlueprintUpdate -tv MyBlueprint-2.0.0            # preview changes a target version would apply (Merge mode)
octo-cli -c PreviewBlueprintUpdate -tv MyBlueprint-2.0.0 -m Safe    # preview in Safe mode
octo-cli -c UpdateBlueprint -tv MyBlueprint-2.0.0                   # apply update with Merge mode
octo-cli -c UpdateBlueprint -tv MyBlueprint-2.0.0 -m Full           # apply Full mode
octo-cli -c UpdateBlueprint -tv MyBlueprint-2.0.0 -dr               # dry-run (no persistent changes)

# Stream data lifecycle (asset repository)
octo-cli -c EnableStreamData
octo-cli -c ActivateArchive -id 69fda707d47638c68edc7fea       # provisions per-archive CrateDB table
octo-cli -c DisableArchive -id 69fda707d47638c68edc7fea        # status-only; data preserved
octo-cli -c EnableArchive -id 69fda707d47638c68edc7fea         # back from Disabled to Activated
octo-cli -c RetryArchiveActivation -id 69fda707d47638c68edc7fea # only from Failed
octo-cli -c DeleteArchive -id 69fda707d47638c68edc7fea          # destructive — drops table, lose data
octo-cli -c DeleteArchive -id 69fda707d47638c68edc7fea -y       # skip confirmation
octo-cli -c DisableStreamData

# Archive data export / import (AB#4230) — move archive row data between tenants/environments.
# Export the whole archive to a ZIP (starts a bot job, waits, then downloads the result).
octo-cli -c ExportArchiveData -tid mytenant -aid 69fda707d47638c68edc7fea -o ./archive-export.zip
# Export only a half-open time slice [fromUtc, toUtc) (ISO-8601 UTC; omit both for whole archive).
octo-cli -c ExportArchiveData -tid mytenant -aid 69fda707d47638c68edc7fea \
  -from 2026-05-11T00:00:00Z -to 2026-05-12T00:00:00Z -o ./archive-slice.zip
# Import a ZIP into a target archive. The target archive MUST be Disabled during import (§7.1):
#   octo-cli -c DisableArchive -id <archiveRtId>
#   octo-cli -c ImportArchiveData -tid mytenant -aid <archiveRtId> -i ./archive-export.zip -w
#   octo-cli -c EnableArchive -id <archiveRtId>
# Default mode is InsertOnly; use Upsert for windowed (time-range / rollup) archives.
octo-cli -c ImportArchiveData -tid mytenant -aid 69fda707d47638c68edc7fea -i ./archive-export.zip -m Upsert -w
# -w waits for the job to finish and surfaces the bot's error (schema mismatch / archive-not-Disabled) verbatim.

# Rollup archive operations (rollup-archives concept §9)
octo-cli -c ListRollupsForArchive -id <sourceArchiveRtId>       # list rollups attached to a source archive
octo-cli -c FreezeRollupArchive -id <rollupRtId> -u 2026-05-11T14:00:00Z   # set FrozenUntil (monotonic)
octo-cli -c UnfreezeRollupArchive -id <rollupRtId>              # clear FrozenUntil (idempotent)
octo-cli -c UnfreezeRollupArchive -id <rollupRtId> -ag          # accept resulting gaps
octo-cli -c RewindRollupWatermark -id <rollupRtId> -t 2026-05-11T10:00:00Z # re-aggregate from boundary

# Optimistic rollup recompute (AB#4184) — recompute a window range with no-mixed-read swap
octo-cli -c RecomputeArchive -id <rollupRtId> -f 2026-05-11T00:00:00Z -t 2026-05-12T00:00:00Z   # recompute [from, to)
octo-cli -c RecomputeArchive -id <rollupRtId> -f 2026-05-11T00:00:00Z -t 2026-05-12T00:00:00Z -s <rtId>  # scoped to one entity
octo-cli -c ListRecomputeJobs -id <rollupRtId>                 # recent recompute jobs (debug failures)
# Backfill a rollup over the ENTIRE history of its source archive (AB#4269 / AB#4286) — no timestamp needed.
# Durable background job: enqueues a recompute of [sourceMin, now) and returns the Pending job id
# immediately (never cancelled by a client HTTP timeout; survives an asset-repo restart).
octo-cli -c BackfillRollup -id <rollupRtId>                    # queue whole-history backfill, return job id
octo-cli -c BackfillRollup -id <rollupRtId> -w                # queue, then poll the job until it completes

# Computed columns (AB#4189) — add a formula-derived column to a live archive and backfill it
octo-cli -c AddComputedColumn -id <archiveRtId> -n powerFactor -f "activePower / apparentPower" -r Double
octo-cli -c AddComputedColumn -id <archiveRtId> -n flag -f "voltage > 230" -r Boolean -x false  # not indexed
octo-cli -c RemoveComputedColumn -id <archiveRtId> -n powerFactor    # rejected if another computed column references it
octo-cli -c UpdateComputedColumnFormula -id <archiveRtId> -n powerFactor -f "activePower / (apparentPower + 1)"  # optimistic backfill + atomic swap

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

# Client role / group assignment (AB#4183) — lets a client_credentials client act as a
# role-protected caller. Roles flow into the client's access token (direct + group-inherited).
octo-cli -c AddClientToRole -id ci-deploy -r DataAnalyst          # assign a role by name
octo-cli -c RemoveClientFromRole -id ci-deploy -r DataAnalyst     # remove a role (-y to skip confirm)
octo-cli -c UpdateClientRoles -id ci-deploy -rids "660...02,660...09"  # replace-all by role id
octo-cli -c AddClientToGroup -id <groupRtId> -cid <clientRtId>    # add client (by RtId) to a group
octo-cli -c RemoveClientFromGroup -id <groupRtId> -cid <clientRtId>

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

# CI/CD workload rollout (Epic 3054, #4053) — chart-version staging + deploy.
# List every workload in the active tenant that uses the chart.
octo-cli -c GetWorkloadsByChart -cn octo-mesh-adapter
# Set ChartVersion on a workload. Does NOT trigger a deploy.
octo-cli -c UpdateWorkloadChartVersion -id <workloadRtId> -cv 1.2.3
# Trigger deploy of the workload through its parent pool.
octo-cli -c DeployWorkload -id <workloadRtId>
# Undeploy (destructive — confirmation prompt; -y to skip).
octo-cli -c UndeployWorkload -id <workloadRtId>

# Pipeline reassignment — move pipelines onto a different adapter when a
# fresh Blueprint provisioned a replacement adapter. Source + target adapter
# must share the same CkTypeId. Each pipeline is moved atomically;
# per-pipeline failures are listed in the output but don't abort the batch.
octo-cli -c MovePipelines -ids p1,p2,p3 -aid <newAdapterRtId>
# Add -rd to also redeploy each moved pipeline onto the new adapter.
# A redeploy failure does not roll the move back — the pipeline already
# points at the new adapter, just hit DeployPipeline manually once the
# adapter is back online.
octo-cli -c MovePipelines -ids p1,p2 -aid <newAdapterRtId> -rd
# -y skips the interactive confirmation prompt.
octo-cli -c MovePipelines -ids p1 -aid <newAdapterRtId> -y

# Rotate the secret of an adapter's pipeline service account (AB#5032 / AB#5048).
# Destructive: the old secret stops working the moment the controller answers.
# A blueprint cannot do this — the secret attribute is runtime state — so this is the path.
octo-cli -c RotateAdapterServiceAccountSecret -id <adapterRtId>
octo-cli -c RotateAdapterServiceAccountSecret -id <adapterRtId> -y   # skip confirmation
# ...then redeploy, or the pipelines keep presenting the withdrawn secret:
octo-cli -c DeployDataFlow -id <dataFlowRtId>

# Multi-tenant ClientCredentials mirroring (Epic 3054, #4047)
# Create a flagged client in octosystem — gets auto-provisioned into every new sub-tenant.
octo-cli -c AddClientCredentialsClient -id ci-deploy -n "CI Deploy" -s <secret> -apic
# List the sub-tenants a client has been mirrored into.
octo-cli -c GetClientMirrors -id ci-deploy
# Backfill: provision the flagged client into every existing sub-tenant (idempotent).
octo-cli -c ProvisionClientInExistingTenants -id ci-deploy
# Manually provision into a single named sub-tenant.
octo-cli -c ProvisionClientInTenant -id ci-deploy -ctid acme
# Manually remove a mirror (destructive — confirmation prompt, -y to skip).
octo-cli -c UnprovisionClientFromTenant -id ci-deploy -ctid acme
# Flip the AutoProvisionInChildTenants flag on an existing client.
# Note: flipping true does NOT auto-backfill — use ProvisionClientInExistingTenants for that.
octo-cli -c SetClientAutoProvision -id ci-deploy -e true

# Client overlay URIs (AB#4209 Step 4) — append operator-scoped URIs onto blueprint-managed
# clients (e.g. local-dev callbacks on the Refinery Studio client) without modifying the
# blueprint. Entries are written with Source = "overlay:<OverlayName>" and survive blueprint
# re-apply via the Step 2a preservation pass. Idempotent — duplicates silently skipped.
octo-cli -c ApplyClientOverlay -id octo-data-refinery-studio -n local-dev \
  -r "http://localhost:4200/auth-callback,http://localhost:4200/silent-callback" \
  -plr "http://localhost:4200/" \
  -co "http://localhost:4200"

# Strip overlay URIs (AB#4209 Step 5) — destructive cleanup before a sanitised tenant dump.
# Without -n: removes every overlay:* source. With -n: removes only overlay:<name>.
# Typical workflow before sharing a dump:
#   octo-cli -c CleanClientOverlays -y && octo-cli -c DumpTenant -tid mytenant ...
# After dumping, restore the local-dev overlays via the cmdlet (idempotent):
#   Apply-IdentityOverlay
octo-cli -c CleanClientOverlays                         # strip all overlay:* (prompts)
octo-cli -c CleanClientOverlays -n local-dev -y         # strip only overlay:local-dev, skip prompt

# OctoTenant identity provider (cross-tenant auth)
octo-cli -c AddOctoTenantIdentityProvider -n "ParentTenant" -e true -ptid <parentTenantId>

# Identity providers with self-registration and default group
octo-cli -c AddAzureEntryIdIdentityProvider -n "Azure" -e true -t <tenantId> -cid <clientId> -cs <secret> -asr false -dgid <groupRtId>

# AI Services — tenant lifecycle (run after EnableCommunication)
octo-cli -c EnableAi
octo-cli -c DisableAi

# AI credential bastion flow (#4123) — register an Anthropic subscription token on a tenant.
#   Two-machine pattern: a tenant admin mints a one-time code via Refinery Studio
#   ("Issue ticket" panel on the AI Console). The code is handed out-of-band to the
#   operator on the bastion machine, who has just completed `claude /login`. The
#   operator then redeems the code together with the freshly minted Anthropic
#   tokens — anonymously, no OctoMesh user session required. The code is the auth
#   artefact; it can be redeemed exactly once and expires after a few minutes.
octo-cli -c RedeemAiTicket \
  -tid meshtest \
  -tc TWUL9NMV7LU8 \
  -at sk-ant-oat01-... \
  -rt sk-ant-ort01-... \
  -aex 2027-01-01T00:00:00Z \
  -rex 2027-12-31T00:00:00Z

# Inspect or revoke the active tenant's lease — both require an authenticated
# tenant-scoped session (current context's TenantId + AiServiceUrl must be set).
octo-cli -c GetAiCredentialsStatus
octo-cli -c RevokeAiCredentials      # destructive — prompts for confirmation
octo-cli -c RevokeAiCredentials -y   # skip confirmation
```

## Communication Services Commands

All communication commands accept plain runtime object IDs (e.g. `69cfa838092b710403248acd`). The SDK client internally constructs composite RtEntityId strings where the server requires them.

### Adapters

| Command | Parameters | Description |
|---------|-----------|-------------|
| `GetAdapters` | `--json` (optional) | List all adapters for the tenant |
| `GetAdapter` | `--identifier <rtId>`, `--json` (optional) | Get adapter configuration |
| `GetAdapterNodes` | | List available pipeline nodes from connected adapters |
| `GetPipelineSchema` | `--adapterId <rtId>`, `--outputFile` (optional) | Get pipeline JSON schema for an adapter |
| `RotateAdapterServiceAccountSecret` | `--identifier <rtId>`, `--yes` (optional) | Rotate the secret of the adapter's pipeline service account (destructive — prompts) |

#### `RotateAdapterServiceAccountSecret` (AB#5032 / AB#5048)

Backs `POST {tenantId}/v1/adapter/{adapterRtId}/serviceAccount/rotateSecret` via
`ICommunicationServicesClient.RotateServiceAccountSecretAsync`. Three properties of it are
deliberate and easy to undo by accident:

- **It is the only supported rotation path.** The secret attribute is runtime state, so a blueprint
  can no longer change a live secret — an attempt fails confusingly. The controller owns both halves
  of the credential (identity client + the tenant's `ServiceAccountConfiguration`), which is why
  neither the CLI nor Studio may reproduce the rotation themselves.
- **The redeploy hint is printed, not swallowed** — as a `Warning`, and the controller's own message
  is relayed verbatim so CLI output and the tenant's audit event cannot drift apart. The adapter
  freezes the credentials into the pipeline's `GlobalConfiguration` at *pipeline registration* and
  never refreshes them, so until `DeployDataFlow` / `DeployPipeline` runs, every pipeline still
  presents the withdrawn secret. Skipping it is what produces "rotation done, still broken".
  When the adapter had no service account at all (`WasCreated`), nothing was invalidated and no
  warning is emitted — a redeploy demand nobody needs is how the real one gets ignored later.
- **No secret is printed**, because the response carries none. Both the controller and
  `Communication.Contracts.Tests` pin that.

### Pipelines

| Command | Parameters | Description |
|---------|-----------|-------------|
| `GetPipelineStatus` | `--identifier <rtId>`, `--json` (optional) | Get deployment state of a pipeline |
| `DeployPipeline` | `--adapterId <rtId>`, `--pipelineId <rtId>`, `--file <path>` | Deploy pipeline definition (YAML/JSON file) |
| `ExecutePipeline` | `--identifier <rtId>`, `--inputFile <path>` (optional) | Execute a pipeline, returns execution ID |
| `GetPipelineExecutions` | `--identifier <rtId>`, `--json` (optional) | List pipeline execution history |
| `GetLatestPipelineExecution` | `--identifier <rtId>`, `--json` (optional) | Get the most recent pipeline execution |
| `GetPipelineDebugPoints` | `--identifier <rtId>`, `--executionId <guid>`, `--json` (optional) | Get debug points for a specific execution |
| `SetPipelineDebug` | `--identifier <rtId>`, `--enabled <true\|false>` | Enable or disable debug capture for a pipeline |
| `GetPipelineDebug` | `--identifier <rtId>`, `--json` (optional) | Get the debug state of a pipeline |

### Triggers

| Command | Parameters | Description |
|---------|-----------|-------------|
| `DeployTriggers` | | Deploy all pipeline triggers for the tenant |
| `UndeployTriggers` | | Undeploy all pipeline triggers for the tenant |

### Pools

| Command | Parameters | Description |
|---------|-----------|-------------|
| `GetPools` | `--json` (optional) | List all pools for the tenant |
| `DeployPool` | `-id <poolRtId>` | Trigger pool deploy (operator creates the CommunicationPool CR); workloads deploy separately |

### Data Flows

| Command | Parameters | Description |
|---------|-----------|-------------|
| `DeployDataFlow` | `--identifier <rtId>` | Deploy a data flow |
| `UndeployDataFlow` | `--identifier <rtId>` | Undeploy a data flow |
| `GetDataFlowStatus` | `--identifier <rtId>`, `--json` (optional) | Get aggregated execution status of a data flow |

### Examples

```bash
# List all adapters
octo-cli -c GetAdapters

# Get adapter config as compact JSON
octo-cli -c GetAdapter --identifier 69cfa838092b710403248acd --json

# Deploy a pipeline from YAML file
octo-cli -c DeployPipeline --adapterId 69cfa838092b710403248acd --pipelineId cc0000000000000000000003 --file pipeline.yaml

# Execute a pipeline and capture the execution ID
octo-cli -c ExecutePipeline --identifier cc0000000000000000000003

# Check data flow status
octo-cli -c GetDataFlowStatus --identifier cc0000000000000000000002

# Enable debug capture for a pipeline
octo-cli -c SetPipelineDebug --identifier cc0000000000000000000003 --enabled true

# Disable debug capture for a pipeline
octo-cli -c SetPipelineDebug --identifier cc0000000000000000000003 --enabled false

# Get pipeline debug state as formatted JSON
octo-cli -c GetPipelineDebug --identifier cc0000000000000000000003

# Get pipeline debug state as compact JSON
octo-cli -c GetPipelineDebug --identifier cc0000000000000000000003 --json
```

## Confirmation Dialogs for Destructive Commands

All destructive commands (Delete, Clean, Reset, Remove, and credential-invalidating verbs such as
Revoke and Rotate) require interactive user confirmation before executing. This prevents accidental
data loss — and accidental lockouts — from typos or wrong IDs. "Destructive" here is not only about
deleting rows: a verb that makes an existing credential stop working belongs in this list too.

### How It Works

- **`IConfirmationService`** (`Services/IConfirmationService.cs`): Interface injected into destructive commands.
- **`ConfirmationService`** (`Services/ConfirmationService.cs`): Prompts the user with `"<message> (y/N): "`. Returns `true` only on "y" or "yes" (case-insensitive). If input is redirected (piped/non-interactive), writes an error to stderr and returns `false`.
- **`--yes` / `-y` flag**: All destructive commands accept this flag to skip the confirmation prompt, enabling CI/automation use.
- **`ToolException.OperationCancelledByUser()`**: Thrown when the user declines confirmation.

### Commands with Confirmation

| Command | Confirmation Message |
|---------|---------------------|
| `DeleteTenant` | `delete tenant '{tenantId}' of {parent scope}` |
| `CleanTenant` | `clean tenant '{tenantId}' of {parent scope}? This will reset it to factory defaults` |
| `ClearTenantCache` | `clear the cache for tenant '{tenantId}' of {parent scope}` |
| `DeleteUser` | `delete user '{name}'` |
| `ResetPassword` | `reset the password for user '{name}'` |
| `RemoveUserFromRole` | `remove user '{name}' from role '{roleName}'` |
| `RemoveClientFromRole` | `remove client '{clientId}' from role '{roleName}'` |
| `DeleteRole` | `delete role '{name}'` |
| `DeleteClient` | `delete client '{clientId}'` |
| `DeleteIdentityProvider` | `delete identity provider '{rtId}'` |
| `DeleteApiResource` | `delete API resource '{name}'` |
| `DeleteApiScope` | `delete API scope '{name}'` |
| `DeleteApiSecretApiResource` | `delete API secret for resource '{name}'` |
| `DeleteApiSecretClient` | `delete API secret for client '{clientId}'` |
| `DeleteArchive` | `delete archive '{archiveRtId}'? The CrateDB table will be dropped and historical data lost` |
| `UninstallBlueprint` | `uninstall blueprint '{name}' from tenant '{tenantId}'[ together with any blueprints that depend on it and any orphaned dependencies]? Locked owned entities will be erased` |
| `UnprovisionClientFromTenant` | `remove client '{clientId}' from child tenant '{childTenantId}' of {parent scope}? …` |
| `RotateAdapterServiceAccountSecret` | `rotate the pipeline service account secret of adapter '{adapterRtId}' in tenant '{tenantId}'? The current secret stops working immediately, and the adapter's pipelines / data flows must be redeployed afterwards` |
| `RevokeAiCredentials` | `revoke AI credential lease for tenant '{tenantId}'? New sessions will fail until a fresh subscription is registered` |

**`{parent scope}`** expands to `parent tenant '<tenant>' at '<scheme://host:port>'` via
`ServiceClientOctoCommand.ParentScopeDescription`. Commands that act on a **child** tenant name it,
because the child alone does not identify the operation: the parent scope comes from the effective
context, which `--context` can change per invocation, and two contexts may carry the same tenant id
against different environments — hence the host, not just the tenant. Commands whose target *is* the
context tenant (`DeleteUser`, `DeleteArchive`, …) do not use it; there is no parent/child split to
disambiguate there.

The prompt text is only visible on a real terminal — `ConfirmationService` writes it with
`Console.Write` and suppresses it entirely when input is redirected. `ParentScopeDescriptionTests`
pins the scope string for that reason.

### Usage Examples

```bash
# Interactive: prompts for confirmation
octo-cli tenants Delete -tid my-tenant

# Skip confirmation (for scripts/CI)
octo-cli tenants Delete -tid my-tenant --yes
octo-cli tenants Delete -tid my-tenant -y
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

## Command Reference Documentation

All command documentation lives in the **command class itself** — there are no sidecar `.docs.md` files. `CommandReferenceGenerator` (Roslyn-based) parses each `.cs` file under `src/ManagementTool/Commands/Implementations/**`, extracts metadata from the constructor (`base(...)` call + `AddArgument(...)` assignments), and walks an optional `GetDocumentation()` override for richer per-command content. The rendered Markdown is published to `octo-documentation` via the `documentation-main-collect` release pipeline.

### What gets extracted automatically

- **Verb** — second-to-last constructor argument (or last, for the no-group ctor overload).
- **Description** — last constructor argument; the renderer uses it as the page's intro paragraph.
- **Group** — first constructor argument (when present); used as the sidebar category.
- **Arguments** — every `_field = CommandArgumentValue.AddArgument(...)` call in the ctor. The field name is captured so `GetDocumentation()` samples can refer back to it.
- **Inherited args** — for commands deriving from `JobWithWaitOctoCommand`, the `-w`/`--wait` flag is appended automatically (mirrored from the base class via `RoslynExtractor.InheritedArgsByBaseClass`).
- **Canonical example** — auto-built from the required arguments (`octo-cli -c <verb> -<short> <long>...`) when no `GetDocumentation()` override is present.

### `GetDocumentation()` override

When the auto-generated content isn't enough, override `GetDocumentation()` on `Command<T>` (defined in `Meshmakers.Common.CommandLineParser`):

```csharp
using Meshmakers.Common.CommandLineParser;

internal class FooCommand : ServiceClientOctoCommand<IFooClient>
{
    private readonly IArgument _targetArg;

    public FooCommand(/* ... */)
        : base(logger, Constants.SomeGroup, "Foo", "Does foo.", options, fooClient, authenticationService)
    {
        _targetArg = CommandArgumentValue.AddArgument("t", "target", ["Target id"], true, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                // Reference _targetArg by field — renaming "t"/"target" in AddArgument auto-updates the sample.
                new CodeSample(
                    arguments: [new CodeSampleArgument(_targetArg, "bar")],
                    description: "Basic usage"),
                new CodeSample(
                    arguments: [new CodeSampleArgument(_targetArg, "baz")],
                    description: "Verbose with expected output",
                    expectedOutput: """
                    ID    NAME
                    42    baz
                    """),
            ],
            Notes:
            [
                "Requires the caller to be in the `TenantOwners` group.",
                "Idempotent — re-running with the same arguments produces the same result.",
            ]);

    public override async Task Execute() { /* ... */ }
}
```

### Conventions

- Use **explicit type names** (`new CodeSample(...)`, `new CodeSampleArgument(...)`) and **named arguments** (`arguments:`, `description:`, `expectedOutput:`) inside the documentation tree. The top-level `new(Samples: …, Notes: …)` keeps the target-typed `new(` because the method return type makes it unambiguous; everything nested below should be explicit so the reader doesn't have to mentally type-check.
- `CodeSample(IEnumerable<CodeSampleArgument> arguments, string description, string? expectedOutput = null)` — arguments are typed bindings, not free-form strings. The renderer composes `octo-cli -c <verb> -<short> "value"...` at format time from the live `IArgument.ShortTerm`. Samples with three or more bindings render multi-line with PowerShell-7 backtick continuation; shorter invocations stay on a single line.
- `CodeSampleArgument(IArgument, string)` for arguments with values; `CodeSampleArgument(IArgument)` for flags. Constructor enforces the right shape against the argument's `MandatoryValuesCount`.
- `ExpectedOutput` is documentation-only — `--help` does not render it (consistent with `kubectl`-style CLIs). `Samples` and `Notes`, in contrast, are rendered by `octo-cli -c <verb> --help` (see [Help Options](#help-options)), so keep both terse enough to read in a terminal.
- Cross-references to non-command pages (concept docs, related sections) belong on handwritten `index.md` landing pages per command-reference section in `octo-documentation`, not on individual command pages — keeps generator output focused on the command itself.
- Skip the override entirely when the auto-canonical example suffices and there are no notes to add — keeps the class clean.

### Where output goes

The generator emits to `bin/Release/documentation/technologyGuide/tools/octo-cli/command-reference/` during CI builds. `octo-cli-pipeline.yml` invokes `createCommandReference.ps1`, and the resulting tree is published as the `_octo-cli` artifact via `handle-artifacts.yml`.

### End-to-end flow

```
   dev edits command class (.cs)              ← only step a developer ever does
            │
            ▼
   octo-cli-CI builds the branch
            │
            ▼
   _octo-cli artifact (contains the regenerated command-reference tree)
            │
            ▼
   Azure DevOps release: documentation-main-collect
   • Task Group UpdateDocumentationRepo:
       Delete files in octo-documentation's command-reference/
         (Contents `**`, excludes `_category_.json` and `**/_category_.json`)
       Copy Files from _octo-cli artifact into the same path
       Commit + push to octo-documentation main
            │
            ▼
   octo-documentation-CI rebuilds Docusaurus
            │
            ▼
   docs.meshmakers.cloud (auto-published)
```

Net effect: editing a `GetDocumentation()` override, an `AddArgument(...)`
help string, or a base() description in any command class is the **single**
step needed to update the public docs site. No manual edits in
`octo-documentation` — handwritten pages there are limited to `intro.md`
and `common-workflows.md`.
