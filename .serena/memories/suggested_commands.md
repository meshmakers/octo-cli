# OctoMesh CLI - Suggested Commands

## IMPORTANT: Windows Environment
This project is developed on Windows. Use Windows-native commands:
- `dir` instead of `ls`
- `type` instead of `cat`
- `cd` for navigation (same as Unix)
- `findstr` instead of `grep`
- PowerShell or CMD for shell operations

## Build Commands

### Local Development Build (ALWAYS USE THIS)
```bash
# Build with local dependencies - THIS IS THE REQUIRED CONFIGURATION FOR DEVELOPMENT
dotnet build Octo.Cli.sln --configuration DebugL
```

**CRITICAL:** The `DebugL` configuration MUST be used for local development. This:
- Sets package versions to `999.0.0`
- Uses the local NuGet feed at `../nuget`
- Ensures compatibility with local OctoMesh service dependencies

### Other Build Configurations
```bash
# Standard debug build (not recommended for local dev)
dotnet build Octo.Cli.sln --configuration Debug

# Production release build
dotnet build Octo.Cli.sln --configuration Release
```

### Clean Build
```bash
# Clean all build artifacts
dotnet clean Octo.Cli.sln --configuration DebugL

# Clean and rebuild
dotnet clean Octo.Cli.sln --configuration DebugL && dotnet build Octo.Cli.sln --configuration DebugL
```

## Running the CLI

### Run via dotnet run
```bash
# Run with arguments
dotnet run --project src/ManagementTool/ManagementTool.csproj --configuration DebugL -- [command] [args]

# Examples:
dotnet run --project src/ManagementTool/ManagementTool.csproj --configuration DebugL -- login
dotnet run --project src/ManagementTool/ManagementTool.csproj --configuration DebugL -- asset:tenant create -tid my-tenant -db my-database
```

### Run the Built Executable
```bash
# After building, run the executable directly
.\bin\DebugL\octo-cli.exe [command] [args]

# Examples:
.\bin\DebugL\octo-cli.exe login
.\bin\DebugL\octo-cli.exe config -isu https://localhost:5003/ -asu https://localhost:5001/ -tid tenant-name
```

## Publishing for Distribution

### Publish for Specific Runtime
```bash
# Windows
dotnet publish src/ManagementTool/ManagementTool.csproj --configuration Release --runtime win-x64

# Linux
dotnet publish src/ManagementTool/ManagementTool.csproj --configuration Release --runtime linux-x64

# Linux ARM64
dotnet publish src/ManagementTool/ManagementTool.csproj --configuration Release --runtime linux-arm64

# macOS
dotnet publish src/ManagementTool/ManagementTool.csproj --configuration Release --runtime osx-x64
```

## Common CLI Commands (Usage Examples)

### Configuration
```bash
# Configure service endpoints and tenant
octo-cli config -isu https://localhost:5003/ -asu https://localhost:5001/ -tid tenant-name

# View current auth status
octo-cli auth-status
```

### Authentication
```bash
# Login to OctoMesh services
octo-cli login
```

### Tenant Management
```bash
# Create a new tenant
octo-cli asset:tenant create -tid tenant-id -db database-name

# Delete a tenant
octo-cli asset:tenant delete -tid tenant-id

# Dump tenant data
octo-cli asset:tenant dump -tid tenant-id -o output-file.zip

# Restore tenant data
octo-cli asset:tenant restore -tid tenant-id -db database-name -i backup-file.zip

# Clean tenant data
octo-cli asset:tenant clean -tid tenant-id

# Clear tenant cache
octo-cli asset:tenant clear-cache -tid tenant-id
```

### Model Management
```bash
# Import construction kit model
octo-cli asset:models import-ck -ckf path/to/model.yaml

# Import runtime model
octo-cli asset:models import-rt -f path/to/model.json

# Export runtime model by query
octo-cli asset:models export-rt-query -q "query" -o output.json

# Export runtime model by deep graph
octo-cli asset:models export-rt-deep -id entity-id -o output.json
```

### Identity Management - Users
```bash
# Get all users
octo-cli identity:users get

# Create a new user
octo-cli identity:users create -e user@example.com -un username -p password

# Update user
octo-cli identity:users update -uid user-id

# Delete user
octo-cli identity:users delete -uid user-id

# Reset user password
octo-cli identity:users reset-password -uid user-id -p new-password

# Add user to role
octo-cli identity:users add-to-role -uid user-id -rid role-id

# Remove user from role
octo-cli identity:users remove-from-role -uid user-id -rid role-id
```

### Identity Management - Roles
```bash
# Get all roles
octo-cli identity:roles get

# Create a new role
octo-cli identity:roles create -n role-name

# Update role
octo-cli identity:roles update -rid role-id

# Delete role
octo-cli identity:roles delete -rid role-id
```

### Identity Management - Clients
```bash
# Get all clients
octo-cli identity:clients get

# Add authorization code client
octo-cli identity:clients add-auth-code -cid client-id -n "Client Name"

# Add client credentials client
octo-cli identity:clients add-client-creds -cid client-id -n "Client Name"

# Add device code client
octo-cli identity:clients add-device-code -cid client-id -n "Client Name"

# Update client
octo-cli identity:clients update -cid client-id

# Delete client
octo-cli identity:clients delete -cid client-id

# Add scope to client
octo-cli identity:clients add-scope -cid client-id -s scope-name
```

### Service Hooks
```bash
# Get all service hooks
octo-cli bots:servicehooks get

# Create a service hook
octo-cli bots:servicehooks create -n "Hook Name" -uri https://webhook.url

# Update service hook
octo-cli bots:servicehooks update -id hook-id -n "New Name"

# Delete service hook
octo-cli bots:servicehooks delete -id hook-id
```

### Communication Services
```bash
# Enable communication services
octo-cli communication:enable -tid tenant-id

# Disable communication services
octo-cli communication:disable -tid tenant-id
```

### Reporting Services
```bash
# Enable reporting services
octo-cli reporting:enable -tid tenant-id

# Disable reporting services
octo-cli reporting:disable -tid tenant-id
```

### DevOps
```bash
# Generate operator certificates
octo-cli devops:certificates generate -o output-directory
```

### Diagnostics
```bash
# Reconfigure log level
octo-cli diagnostics:log-level -l Debug
```

## Testing Commands

**Note:** This solution does not include test projects. Testing is performed at the service level in other repositories.

## Git Commands (Common Operations)

```bash
# Check status
git status

# Create a feature branch
git checkout -b feature/my-feature

# Commit changes
git add .
git commit -m "Description of changes"

# Push changes
git push origin feature/my-feature

# Pull latest changes
git pull origin main
```

## Utility Commands for Development

### Restore NuGet Packages
```bash
dotnet restore Octo.Cli.sln
```

### List Projects in Solution
```bash
dotnet sln Octo.Cli.sln list
```

### Clean Build Artifacts
```bash
# Clean bin and obj folders
dotnet clean Octo.Cli.sln --configuration DebugL

# Or manually (PowerShell)
Get-ChildItem -Path . -Include bin,obj -Recurse -Directory | Remove-Item -Recurse -Force
```

### Check .NET Version
```bash
dotnet --version
dotnet --info
```

### Inspect Assembly
```bash
# View assembly info
dotnet .\bin\DebugL\octo-cli.dll --help
```
