## Examples

```bash
octo-cli -c CheckDependencies -cn PublicGitHubCatalog -m "Industry.Energy-2.0.0"
```

**Output:**

```
Industry.Energy v2.0.0  [INSTALL]
  Industry.Basic v2.1.0  [INSTALL]
    Basic v2.0.2  [NONE] (installed: v2.0.2)
    System v2.0.7  [NONE] (service-managed: v2.0.7)

Models to import (2):
  Industry.Basic-2.1.0
  Industry.Energy-2.0.0
```
