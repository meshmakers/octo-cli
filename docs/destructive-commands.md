---
description: How destructive commands ask for confirmation, the --yes flag, the confirmation messages and the parent scope text.
applies_to: src/ManagementTool/Services/*Confirmation*.cs, src/ManagementTool/ToolException.cs
---

# Confirmation dialogs for destructive commands

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
| `UninstallBlueprint` | `uninstall blueprint '{name}' from tenant '{tenantId}'[ together with any blueprints that depend on it and any orphaned dependencies]? Locked owned entities and unedited tenant-owned seed entities will be erased (see --dry-run)` (no prompt with `--dry-run`) |
| `UnprovisionClientFromTenant` | `remove client '{clientId}' from child tenant '{childTenantId}' of {parent scope}? …` |
| `SwitchServiceAccountsToImpersonation` (`--apply` without `-y`) | `switch the pipeline service accounts of tenant '{tenantId}' to impersonation? Stored client secrets are cleared and the old secrets are invalidated` |
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
