## Examples

Returns the debug point node tree for a specific pipeline execution:

```powershell
octo-cli -c GetPipelineDebugPoints \
  -id "cc0000000000000000000003" \
  -eid "7c011b03-b738-4be7-948c-78ee28e4b233"
```

Output as compact JSON:

```powershell
octo-cli -c GetPipelineDebugPoints \
  -id "cc0000000000000000000003" \
  -eid "7c011b03-b738-4be7-948c-78ee28e4b233" \
  -j
```
