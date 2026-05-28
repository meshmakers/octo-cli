## Examples

Works for all provider types. Fetches the existing provider, preserves type-specific properties, and applies the changes:

```powershell
octo-cli -c UpdateIdentityProvider -id "provider-id" -n "Updated Name" -e true -asr false -dgid "<default-group-rtid>"
```

For OAuth-based providers, you can also update client credentials:

```powershell
octo-cli -c UpdateIdentityProvider -id "provider-id" -n "Updated Name" -e true -cid "new-client-id" -cs "new-client-secret"
```
