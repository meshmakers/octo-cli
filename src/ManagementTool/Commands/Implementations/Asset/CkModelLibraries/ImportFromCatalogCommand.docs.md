## Examples

Import with wait:

```powershell
octo-cli -c ImportFromCatalog -cn PublicGitHubCatalog -m "Industry.Energy-2.0.0" -w
```

Import without waiting (returns job IDs):

```powershell
octo-cli -c ImportFromCatalog -cn PublicGitHubCatalog -m "Industry.Energy-2.0.0"
```
