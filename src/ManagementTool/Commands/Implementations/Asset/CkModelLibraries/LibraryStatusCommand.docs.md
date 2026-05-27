## Examples

Full status:

```bash
octo-cli -c LibraryStatus
```

Only models needing attention:

```bash
octo-cli -c LibraryStatus -na
```

**Output:**

```
NAME                      INSTALLED    CATALOG      STATE           ACTION
------------------------- ------------ ------------ --------------- --------------------
System                    2.0.7        2.0.7        Available       Service-Managed
Basic                     2.0.2        2.0.2        Available       -
Industry.Energy           -            2.0.0        Not Installed   Install
```
