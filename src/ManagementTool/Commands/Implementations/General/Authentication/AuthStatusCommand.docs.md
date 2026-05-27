## Examples

```powershell
octo-cli -c AuthStatus
```

## Notes

- No parameters required. Reports the JWT claims of the current access token, including an `Auth Method` line indicating whether the token came from the device-code flow or `client_credentials`. For `client_credentials` tokens (no `sub` claim), the User Info section is omitted because the token is not user-bound.
