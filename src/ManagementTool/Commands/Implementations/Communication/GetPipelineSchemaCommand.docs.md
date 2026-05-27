## Examples

Returns the JSON Schema describing valid pipeline definitions for a specific adapter:

```powershell
octo-cli -c GetPipelineSchema -aid "69cfa838092b710403248acd"
```

Write schema to a file instead of stdout:

```powershell
octo-cli -c GetPipelineSchema -aid "69cfa838092b710403248acd" -o "./pipeline-schema.json"
```
