# OctoMesh CLI - Task Completion Guidelines

## What to Do When a Task is Completed

When you finish implementing a feature or fixing a bug in the octo-cli project, follow these steps to ensure code quality and successful integration:

## 1. Build the Project (REQUIRED)

**ALWAYS use the DebugL configuration for local development:**

```bash
dotnet build Octo.Cli.sln --configuration DebugL
```

### Why DebugL?
- Sets package versions to `999.0.0`
- Uses local NuGet feed at `../nuget`
- Required for compatibility with local OctoMesh service dependencies
- **THIS IS NON-NEGOTIABLE for local development**

### Build Validation
The build serves as the primary quality gate:
- **TreatWarningsAsErrors** is enabled - all warnings must be fixed
- **Nullable reference types** are enforced - null safety is validated
- All compiler errors must be resolved

### Expected Output
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

If the build fails:
1. Read the error/warning messages carefully
2. Fix all issues
3. Rebuild until successful

## 2. Testing (Note: No Tests in This Project)

**Important:** This solution does not include test projects. Testing is performed at the service level in other OctoMesh repositories.

If you're adding new functionality:
- Consider whether integration tests should be added to the relevant service repository
- Manual testing may be required by running the CLI commands

### Manual Testing Workflow
After building, test your changes:

```bash
# Run the built executable
.\bin\DebugL\octo-cli.exe [your-command] [args]

# Or via dotnet run
dotnet run --project src/ManagementTool/ManagementTool.csproj --configuration DebugL -- [your-command] [args]
```

**Example manual test checklist:**
- [ ] Command executes without errors
- [ ] Command produces expected output
- [ ] Logging messages are clear and informative
- [ ] Error handling works correctly
- [ ] Authentication flow works (if applicable)

## 3. Code Formatting and Linting

### No Dedicated Tools Configured
This project does not have:
- ❌ .editorconfig file
- ❌ Code formatter configuration
- ❌ Dedicated linting tools

### Manual Code Quality Checks
Before committing, verify your code follows project conventions:

#### Naming Conventions
- [ ] Classes use PascalCase: `CreateUser`, `ServiceClientOctoCommand`
- [ ] Private fields use _camelCase: `_logger`, `_eMailArg`
- [ ] Methods and properties use PascalCase: `Execute()`, `PreValidate()`
- [ ] Parameters and local variables use camelCase: `logger`, `eMail`

#### C# Features
- [ ] Using file-scoped namespaces
- [ ] Using primary constructors where appropriate
- [ ] Using modern collection expressions `[]` instead of `new[]`
- [ ] Async methods properly use `await` (no `.Result` or `.Wait()`)
- [ ] Nullable reference types handled correctly (no `!` operators unless justified)

#### Code Organization
- [ ] Commands inherit from appropriate base class:
  - `ServiceClientOctoCommand<T>` for service interactions
  - `JobOctoCommand` for job-based operations
  - `JobWithWaitOctoCommand` for jobs with wait
- [ ] Commands registered in `Program.cs` dependency injection
- [ ] One class per file, file name matches class name

#### Logging
- [ ] Using structured logging with placeholders
- [ ] Log messages use single quotes around values: `'{Name}'`
- [ ] Appropriate log levels used (Information, Error, Critical)

## 4. Git Workflow

### Before Committing
1. **Build successfully** (with DebugL configuration)
2. **Review your changes**:
   ```bash
   git status
   git diff
   ```
3. **Ensure no unintended files** are staged (e.g., bin/, obj/, user settings)

### Committing Changes
```bash
# Stage relevant files
git add src/ManagementTool/Commands/Implementations/YourNewCommand.cs
git add src/ManagementTool/Program.cs  # If you registered a new command

# Commit with descriptive message
git commit -m "Add new command for managing X

- Implements YourNewCommand class
- Adds command registration in Program.cs
- Follows ServiceClientOctoCommand pattern"

# Push to your branch
git push origin feature/your-feature-name
```

### Commit Message Guidelines
- **First line:** Brief summary (50 chars or less)
- **Body:** Detailed explanation of what and why
- **Reference:** Include Azure DevOps work item if applicable: `AB#1234`

## 5. Documentation Updates

If your changes affect usage:

### Update CLAUDE.md (if applicable)
The `CLAUDE.md` file in the repository root provides guidance to AI assistants. Update if:
- New command categories are added
- New architectural patterns are introduced
- Build/run procedures change

### Update README (if exists)
Check if there's a README.md that needs updating with:
- New command examples
- Changed CLI syntax
- New configuration options

## 6. Pre-Pull Request Checklist

Before creating a pull request:

- [ ] Code builds successfully with `DebugL` configuration
- [ ] All compiler warnings resolved (build shows 0 warnings)
- [ ] Manual testing completed for new/changed functionality
- [ ] Code follows project naming and style conventions
- [ ] New commands registered in `Program.cs`
- [ ] Logging statements are clear and use structured logging
- [ ] No sensitive information in code (credentials, tokens, etc.)
- [ ] Git commit messages are descriptive
- [ ] CLAUDE.md updated if architectural changes made

## 7. CI/CD Pipeline

The project uses Azure DevOps pipelines defined in `devops-build/octo-cli-pipeline.yml`.

### Pipeline Triggers
- Branches: `dev/*`, `test/*`, `main`
- Tags: Release tags starting with `r`

### Pipeline Steps
1. Version update
2. Checkout code
3. Set version
4. Publish for multiple runtimes:
   - linux-x64
   - linux-arm64
   - osx-x64
   - win-x64
5. Create NuGet package
6. Handle artifacts

### What Gets Built in CI
The pipeline publishes **Release** configuration (not DebugL):
- Single-file executables
- Self-contained (includes .NET runtime)
- Platform-specific binaries

## 8. Dependency Management

### When Adding New Dependencies

1. **Add PackageReference** to `.csproj`:
   ```xml
   <PackageReference Include="Some.Package" Version="1.2.3" />
   ```

2. **Consider version management:**
   - Use `$(OctoVersion)` for internal OctoMesh packages
   - Use `$(MeshmakerVersion)` for Meshmakers packages
   - Use explicit versions for third-party packages

3. **Restore and verify:**
   ```bash
   dotnet restore Octo.Cli.sln
   dotnet build Octo.Cli.sln --configuration DebugL
   ```

## 9. Common Pitfalls to Avoid

❌ **Don't:**
- Build with `Debug` or `Release` configuration for local development (use `DebugL`)
- Use `.Result` or `.Wait()` on async operations (use `await`)
- Hardcode configuration values (use `IOptions<OctoToolOptions>`)
- Skip authentication in commands (base classes handle this)
- Use unstructured log messages (use structured logging)
- Commit `bin/` or `obj/` directories

✅ **Do:**
- Always build with `DebugL` configuration locally
- Use dependency injection for all services
- Inherit from appropriate command base class
- Follow async/await patterns throughout
- Use structured logging with placeholders
- Keep commands focused and single-purpose

## 10. Summary Checklist

When your task is complete, verify:

```
[ ] Code builds successfully: dotnet build Octo.Cli.sln --configuration DebugL
[ ] Build shows 0 warnings, 0 errors
[ ] Manual testing completed
[ ] Code follows project conventions (naming, structure, patterns)
[ ] Logging is clear and structured
[ ] New commands registered in Program.cs
[ ] Git commits are descriptive and clean
[ ] No sensitive data in code
[ ] Ready for code review / pull request
```

## Quick Command Reference

```bash
# Clean build
dotnet clean Octo.Cli.sln --configuration DebugL && dotnet build Octo.Cli.sln --configuration DebugL

# Run after build
.\bin\DebugL\octo-cli.exe [command] [args]

# Check git status
git status
git diff

# Stage and commit
git add [files]
git commit -m "Description"

# Push
git push origin [branch-name]
```
