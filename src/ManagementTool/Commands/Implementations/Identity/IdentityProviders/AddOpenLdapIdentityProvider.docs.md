## Examples

```powershell
octo-cli -c AddOpenLdapIdentityProvider \
  -n "OpenLDAP" \
  -h "ldap.example.com" \
  -p 389 \
  -ubdn "cn=users,dc=example,dc=com" \
  -uan "uid" \
  -e true \
  -asr false
```
