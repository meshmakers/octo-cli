# AGENTS.md

Instructions for coding agents in this repository. Detail lives in `docs/`; read the matching file before changing the
code it covers.

## What this repo is

Octo CLI (`octo-cli`) is a .NET 10 command-line management tool for the Octo Mesh Platform. It provides commands for
managing tenants, users, roles, clients, identity providers, and other platform resources across all Octo services.

## Read before you change

<!-- >>> generated: routing -->
| When you change | Read first |
|---|---|
| `src/ManagementTool/*.cs`, `**/Services/**`, `*.csproj`, `Octo.Cli.sln`, `devops-build/**` | `docs/architecture.md` |
| `**/Services/{Authentication,Context,LoginReplacement,SecretMasker}*.cs`, `**/Implementations/General/**`, `OctoCommandParser.cs` | `docs/authentication-and-contexts.md` |
| `**/Asset/{CkModelLibraries,Models,Blueprints}/**`, `CkModelFileInspector.cs` | `docs/ck-models-and-blueprints.md` |
| `src/CommandReferenceGenerator/**`, `tests/CommandReferenceGenerator.Tests/**`, `src/ManagementTool/Commands/**` | `docs/command-help-and-reference.md` |
| `*Confirmation*.cs`, `ToolException.cs`, `{Delete,Remove,Revoke,Rotate,Clean,Reset}*.cs`, `{Un*,Cancel,Move,Reprotect,FixAll,Repair,Restore,Clear,Switch}*.cs` | `docs/destructive-commands.md` |
| `**/Asset/{GraphQl,Knowledge}/**`, `MachineReadableOutput.cs`, `**/Fixtures/**` | `docs/graphql-and-knowledge.md` |
| `**/Implementations/Identity/**`, `{Runner,ForbiddenResponseMessage,CkMethodErrorText,CkMethodFallback}.cs` | `docs/identity-commands.md` |
| `**/Implementations/{Communication,Ai}/**`, `**/Asset/PlatformFiles/**` | `docs/platform-files-and-communication.md` |
| `{ErrorOutputRedactor,RedactedLogOutput,SecretInput,SecretConsole,StandardStreams,SecretMasker}.cs`, `**/Identity/{Users,ApiSecrets}/**` | `docs/secrets-and-output.md` |
| `**/Asset/{Tenants,TimeSeries,Operations}/**`, `ChildTenantAccess.cs` | `docs/tenant-data-operations.md` |
<!-- <<< end generated: routing -->

Background, no path trigger: [docs/cli-usage-examples.md](docs/cli-usage-examples.md) holds command examples for
contexts, login, tenants, blueprints, archives, groups, clients, workloads and AI services; use it for the argument
forms of a command, and for the exact arguments run `octo-cli -c <command> --help`.

## Build & test

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

### Build Configurations

- **Debug/Release**: Standard configurations
- **DebugL**: Local development mode that sets version to 999.0.0 and uses local NuGet sources from `../nuget`

Build and test locally in `DebugL`; `Release` (used by CI) resolves the service-client packages from the private feed.
A new project must opt into `DebugL` in its `.csproj` and in `Octo.Cli.sln`
([docs/architecture.md](docs/architecture.md), "Project Structure"). Tests use a throwaway directory instead of the
real `~/.octo-cli` (`ContextManager(string baseDirectory)`).

## Before you commit

- Build and all tests are green (`dotnet test Octo.Cli.sln -c DebugL`).
- The documentation changes with the code, in English:
  - a new or changed command: its help text and `GetDocumentation()` samples and notes, which are the public command
    reference ([docs/command-help-and-reference.md](docs/command-help-and-reference.md)); never edit generated
    reference pages by hand;
  - authentication or context changes: [docs/authentication-and-contexts.md](docs/authentication-and-contexts.md);
  - configuration, project structure or environment variables: [docs/architecture.md](docs/architecture.md);
  - behaviour covered by another doc in the table above: that doc;
  - documentation, code comments and help text stay in English.
- Run `Test-OctoAgentDocs` (octo-tools) after changing `AGENTS.md` or `docs/`; `Test-OctoAgentDocs -Fix` regenerates
  the routing table. The shim file for Claude Code is generated: edit this file, not the shim.
- This repository is public: no work item ids, internal hostnames, customer or tenant names, or secrets in `AGENTS.md`,
  `docs/`, help text or tests.

## Rules

These rules bind new and changed code. Known exceptions in existing code: `ConfigCommand` reads the active context,
`Create*ApiSecret*` show a generated secret once on purpose, `MigrateIfNeeded` writes `contexts.json` directly, and some
older destructive commands do not ask for confirmation.

- **No credentials in output.** New code must not print a token, password or client secret in a log line, an exception
  text or a response body. Errors pass the central `ErrorOutputRedactor`; add no per-command redaction and write no
  response body through `Console`. Mask with `SecretMasker` ([docs/secrets-and-output.md](docs/secrets-and-output.md)).
- **Secrets off the command line.** New commands read a password or secret through `Services/SecretInput` (stdin flag,
  environment variable, hidden prompt), and help samples show no secret as an argument
  ([docs/secrets-and-output.md](docs/secrets-and-output.md)).
- **Destructive commands ask first.** New and changed destructive commands (delete, clean, reset, remove, revoke, rotate
  and other credential-invalidating verbs) MUST use `IConfirmationService` and accept `--yes`/`-y`
  ([docs/destructive-commands.md](docs/destructive-commands.md)).
- **One token, one tenant.** New code addressing tenant X sends no token issued for another tenant and falls back to no
  other token when the exchange is denied; get service clients from `AuthenticationService.ApplyAsync`, and clients for
  a child tenant from `ChildTenantAccess` ([docs/authentication-and-contexts.md](docs/authentication-and-contexts.md)).
- **Contexts.** New code acting on "the context in use" uses `GetEffectiveContext()` / `SaveEffectiveContext()`, and
  writes `contexts.json` through `ContextManager.Mutate`
  ([docs/authentication-and-contexts.md](docs/authentication-and-contexts.md)).
- **Top-level arguments.** A new top-level argument is declared on the parser's top layer and needs the same hoisting
  treatment as `--context`, which has no short form
  ([docs/authentication-and-contexts.md](docs/authentication-and-contexts.md)).
- **GraphQL documents.** Validate new or changed hand-written GraphQL documents in tests with
  `AssetRepositoryGraphQlSchema` ([docs/graphql-and-knowledge.md](docs/graphql-and-knowledge.md)).
- **Command reference.** It is generated from the command classes: write argument help inline in `AddArgument`
  collection literals (`Constants.*` only inside interpolations)
  ([docs/command-help-and-reference.md](docs/command-help-and-reference.md)).
