## Examples

Provision yourself in a target tenant with all available roles and TenantOwners group membership:

```powershell
octo-cli -c ProvisionCurrentUser -ttid "customer-project"
```

## Notes

All admin provisioning commands must be run from the **system tenant context**.
