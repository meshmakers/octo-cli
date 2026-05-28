## Examples

Delegates authentication to a parent tenant for cross-tenant access.

```powershell
octo-cli -c AddOctoTenantIdentityProvider -n "Parent Tenant Auth" -ptid "octosystem" -e true -asr true -dgid "<default-group-rtid>"
```

## See Also

- [Cross-Tenant Authentication](../../../../identityService/cross-tenant-authentication.md) — configure `AddOctoTenantIdentityProvider` with the parent tenant ID to allow users from that tenant to log in to the child tenant.
