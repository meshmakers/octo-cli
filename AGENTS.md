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
| `src/CommandReferenceGenerator/**`, `tests/CommandReferenceGenerator.Tests/**`, `src/ManagementTool/Commands/*.cs` | `docs/command-help-and-reference.md` |
| `*Confirmation*.cs`, `ToolException.cs` | `docs/destructive-commands.md` |
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

- **No credentials in output.** Never print a token, password or client secret, in a log line, an exception text or a
  response body. Errors pass the central `ErrorOutputRedactor`; do not add per-command redaction and do not write
  response bodies through `Console`. Mask values with `SecretMasker`
  ([docs/secrets-and-output.md](docs/secrets-and-output.md)).
- **Secrets never on the command line.** A command that takes a password or secret reads it through
  `Services/SecretInput` (stdin flag, environment variable, hidden prompt); help samples never show a secret as an
  argument ([docs/secrets-and-output.md](docs/secrets-and-output.md)).
- **Destructive commands ask first.** New and changed destructive commands (delete, clean, reset, remove, revoke,
  rotate and other credential-invalidating verbs) MUST use `IConfirmationService` and accept `--yes`/`-y`; older
  commands are not all covered yet ([docs/destructive-commands.md](docs/destructive-commands.md)).
- **One token, one tenant.** A command addressing tenant X never sends a token of another tenant and never falls back to
  the context's own token. `AuthenticationService.ApplyAsync` hands the context's token (exchanged when needed) to the
  injected service clients; clients for a child tenant get theirs from `ChildTenantAccess`
  ([docs/authentication-and-contexts.md](docs/authentication-and-contexts.md)).
- **Effective context, atomic writes.** Code acting on "the context in use" uses `GetEffectiveContext()` /
  `SaveEffectiveContext()`, not the active one; every write of `contexts.json` goes through `ContextManager.Mutate`
  ([docs/authentication-and-contexts.md](docs/authentication-and-contexts.md)).
- **Top-level arguments.** `--context` is declared on the parser's top layer and has no short form; a new top-level
  argument needs the same hoisting treatment
  ([docs/authentication-and-contexts.md](docs/authentication-and-contexts.md)).
- **GraphQL documents are tested against the schema.** Validate every hand-written GraphQL document with
  `AssetRepositoryGraphQlSchema` ([docs/graphql-and-knowledge.md](docs/graphql-and-knowledge.md)).
- **Public docs come from the command classes.** The command reference is generated from the command classes; keep help
  strings inline in `AddArgument` collection literals
  ([docs/command-help-and-reference.md](docs/command-help-and-reference.md),
  [docs/secrets-and-output.md](docs/secrets-and-output.md)).
