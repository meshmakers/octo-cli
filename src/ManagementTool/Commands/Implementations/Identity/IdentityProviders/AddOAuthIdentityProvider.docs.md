## Examples

Supports Google, Microsoft, Facebook:

```powershell
octo-cli -c AddOAuthIdentityProvider \
  -n "Google Login" \
  -t "google" \
  -cid "your-client-id" \
  -cs "your-client-secret" \
  -e true \
  -asr true \
  -dgid "<default-group-rtid>"
```
