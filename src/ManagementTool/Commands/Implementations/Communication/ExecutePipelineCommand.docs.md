## Examples

Triggers a pipeline execution and returns the execution ID:

```powershell
octo-cli -c ExecutePipeline -id "cc0000000000000000000003"
```

Optionally provide input data from a file:

```powershell
octo-cli -c ExecutePipeline -id "cc0000000000000000000003" -f "./input.json"
```
