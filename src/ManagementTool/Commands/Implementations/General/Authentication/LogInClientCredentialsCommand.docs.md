## Examples

One-time setup — create a per-tenant client (must already be authenticated as a user):

```powershell
octo-cli -c AddClientCredentialsClient -id "my-script-client" -n "Script client" -s "<secret>"
```

Headless run — export credentials, then login:

```powershell
$env:OCTO_CLI_CLIENT_ID    = "my-script-client"
$env:OCTO_CLI_CLIENT_SECRET = "<secret>"
octo-cli -c LogInClientCredentials
```

Or using shell args directly (args take precedence over env vars):

```powershell
octo-cli -c LogInClientCredentials -id "my-script-client" -s "<secret>"
```

## Notes

Tenant comes from the active context. Switch contexts (e.g. `octo-cli -c UseContext -n prod`) to target a different tenant. The login fails fast if the active context has no `TenantId`.

The client is per-tenant. If a script needs to address multiple tenants, create one `client_credentials` client per tenant and switch contexts before each login.

No refresh token is issued (per OAuth2 spec for `client_credentials`). While `OCTO_CLI_CLIENT_ID` and `OCTO_CLI_CLIENT_SECRET` remain set in the environment, octo-cli automatically re-acquires the token when it expires — long-running scripts do not need to call `LogInClientCredentials` again between commands. If the env vars are unset, the next API call after expiry returns 401 and you must re-run the login.

The token is stored in the active context's `Authentication` block, exactly like a device-code token.
