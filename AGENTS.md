# AGENTS.md

Instructions for coding agents in this repository. Detail lives in `docs/`; read the matching file before changing the
code it covers.

## What this repo is

Octo CLI (`octo-cli`) is a .NET 10 command-line management tool for the Octo Mesh Platform. It provides commands for managing tenants, users, roles, clients, identity providers, and other platform resources across all Octo services.

## Read before you change

<!-- >>> generated: routing -->
| When you change | Read first |
|---|---|
| `src/ManagementTool/*.cs`, `src/ManagementTool/Services/**`, `**/*.csproj`, `Octo.Cli.sln`, `devops-build/**` | `docs/architecture.md` |
| `src/ManagementTool/Services/{Authentication,Context,LoginReplacement,SecretMasker}*.cs`, `src/ManagementTool/Commands/Implementations/General/**`, `src/ManagementTool/OctoCommandParser.cs` | `docs/authentication-and-contexts.md` |
| `src/ManagementTool/Commands/Implementations/Asset/{CkModelLibraries,Models,Blueprints}/**`, `src/ManagementTool/Services/CkModelFileInspector.cs` | `docs/ck-models-and-blueprints.md` |
| `src/CommandReferenceGenerator/**`, `tests/CommandReferenceGenerator.Tests/**`, `src/ManagementTool/Commands/*.cs` | `docs/command-help-and-reference.md` |
| `src/ManagementTool/Services/*Confirmation*.cs`, `src/ManagementTool/ToolException.cs` | `docs/destructive-commands.md` |
| `src/ManagementTool/Commands/Implementations/Asset/{GraphQl,Knowledge}/**`, `src/ManagementTool/Services/MachineReadableOutput.cs`, `tests/ManagementTool.Tests/Fixtures/**` | `docs/graphql-and-knowledge.md` |
| `src/ManagementTool/Commands/Implementations/Identity/**`, `src/ManagementTool/{Runner,ForbiddenResponseMessage,CkMethodErrorText,CkMethodFallback}.cs` | `docs/identity-commands.md` |
| `src/ManagementTool/Commands/Implementations/{Communication,Ai}/**`, `src/ManagementTool/Commands/Implementations/Asset/PlatformFiles/**` | `docs/platform-files-and-communication.md` |
| `src/ManagementTool/Services/{ErrorOutputRedactor,RedactedLogOutput,SecretInput,SecretConsole,StandardStreams}.cs`, `src/ManagementTool/Commands/Implementations/Identity/**` | `docs/secrets-and-output.md` |
| `src/ManagementTool/Commands/Implementations/Asset/{Tenants,TimeSeries,Operations}/**`, `src/ManagementTool/Services/ChildTenantAccess.cs` | `docs/tenant-data-operations.md` |
<!-- <<< end generated: routing -->

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

## Before you commit

### Documentation Guidelines

**CRITICAL REQUIREMENT:** Documentation MUST be updated after EVERY change. This is mandatory, not optional.

#### Language Requirement

All documentation MUST be written in **English**. This includes:
- README.md files
- Code comments
- API documentation
- This CLAUDE.md file

#### Mandatory Documentation Updates

After making ANY code changes, you MUST update:

1. **For New Commands**: Document command usage, parameters, and examples
2. **For Bug Fixes**: Update troubleshooting sections if applicable
3. **For Authentication Changes**: Update the Authentication Flow section above
4. **For Configuration Changes**: Update the Configuration section

#### Key Files to Update

| File | When to Update |
|------|----------------|
| `README.md` | Project overview, usage examples |
| `CLAUDE.md` | Project structure, architecture changes |
| Command help text | When command parameters change |

## Rules

