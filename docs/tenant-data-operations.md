---
description: Large uploads, restore outcome, rollup validate and repair, dump through the operation handle, scratch tenants.
applies_to: src/ManagementTool/Commands/Implementations/Asset/{Tenants,TimeSeries,Operations}/**, src/ManagementTool/Services/ChildTenantAccess.cs
---

# Tenant data operations

## Large uploads: Restore and ImportArchiveData (AB#6970, Bug AB#6786)

`Restore` (`Asset/Tenants/RestoreTenant.cs`) and `ImportArchiveData` (`Asset/TimeSeries/ImportArchiveData.cs`) upload through
the SDK's `RestoreRepositoryWithTusAsync` / `StartImportArchiveDataWithTusAsync`, which send the file in parts of at most
50 MiB (SDK `BotServiceClientOptions.TusPartSizeBytes`), so a backup of several GB passes the ingress body limit (100 MiB on
staging-1, prod-1, prod-2; before, one PATCH of the whole file was refused with 413 and the command failed later on an
"empty (0 bytes)" file). MCP uses the same SDK methods.

- **`-ps/--part-size-mib <n>`** (both commands, 1 to 90, default 50) sets the part size for this run. The commands receive
  `IOptions<BotServiceClientOptions>` and assign the value before the upload (the bot client reads the same options object);
  `TusUploadSupport.ApplyPartSize` turns an out-of-range value into a `ToolException` before anything is sent. There is no
  config-file or environment setting.
- **A refused upload** surfaces as the SDK's `TusUploadException` (status, part number, offset, e.g. "Upload of part 3 of 70
  (offset 104857600, at most 52428800 bytes) was refused: 413 RequestEntityTooLarge"). `Runner` logs the message (exit -3);
  no job is started. A lost connection is retried by the SDK from the server's offset.
- **Progress** is logged by `UploadProgressLogger`: one line per 5 percent or after 30 quiet seconds, with sent / total size
  and the average rate (the SDK reports about once per MiB).
- Tests: `TusUploadCommandTests` (fake `IBotServicesClient`: part size option, refusal, progress). The tus test server lives in
  the SDK tests (internal there); the wire behaviour (parts, 413, resume) is tested in the SDK, not repeated here.

## Restore outcome (AB#6969)

`Restore -w` reads the finished job (`JobDto`: `Outcome`, `FailedArchives`, `NotCheckedRollups`, derived `HasWarnings`) and does not print a
plain "restored" when the restore only partly succeeded. `RestoreOutcome.ThrowIfWarnings` logs every failed archive and every blocking
not-checked rollup as an error and ends with a `ToolException` (exit -5): `Tenant 'x' (database 'y') restored with N failed archive(s): <ids>;
M rollup(s) not checked: <id> [Cause]. The tenant is not fully restored.` (`restored with warnings` when only rollups are blocking).
Informational not-checked rollups (never activated) are logged as `not checked (info)` and do not change the result. A bot without these
fields (older bot, no `-w`) behaves as before. Tests: `RestoreOutcomeTests`; `RestoreOutcomeContractTests` feeds the wire JSON captured in the bot tests (failed archive, clean, informational) through the real `BotServicesClient` into the command.

## Rollup validate and repair (AB#6968)

`ValidateRollups`, `RepairRollups` and `GetRollupRepairStatus` (`Asset/TimeSeries/`) are the operator check for the rollup state after a
CrateDB restore (DR runbook: `ValidateRollups`, then `RepairRollups` as a dry run, then `RepairRollups --apply --wait`). They call
`IStreamDataServicesClient` (`ValidateRollupsAsync`, `PlanRollupRepairAsync`, `EnqueueRollupRepairAsync`, `GetRollupRepairRequestAsync`;
asset repository `streamdata/rollups/validate|repair`).

- **`ValidateRollups`** is read only: `-id` (optional comma list, default all rollups), `-j` JSON, `-ff/--failOnFindings` (exit -5 when a
  rollup is inconsistent; without it findings are printed and the exit code stays 0). Checks: pointer without rows, rows above the
  pointer, watermark ahead of the rows, missing generation map.
- **`RepairRollups`** is a **dry run by default** (lists the planned steps per rollup, asks nothing, changes nothing). `-a/--apply`
  asks for confirmation (`-y` skips it) and **requests** the repair: the server executes it in the background (nothing runs inside the
  call, so an ingress timeout cannot cut a long recompute) and answers with a request id. `-w/--wait` polls the request (5 s, up to 4 h)
  until Done or Failed and prints the final report incl. the validation after the repair; exit -5 when the request failed, a step failed
  or the tenant still does not validate ok. Without `--wait` the request id is printed. A second `--apply` while a repair is active
  returns the active request. Idempotent.
- **`GetRollupRepairStatus -rid <requestId>`** shows the state (Pending, Running, Done, Failed) and, once ended, the final report;
  `-ff/--failOnFailed` makes a failed request exit -5; `-j` JSON. Read only.
- **NotChecked:** a rollup the validator could not check is never printed as OK. A blocking gap (NoLiveTable, SourceMissing; `isBlocking`) is listed in `Not checked (N):` with `[Cause] reason`, the summary says `N NOT CHECKED`, and `-ff` exits -5 (it follows the report's `IsOk`). A never-activated rollup (NeverActivated, informational) has no data: it is listed separately as `Not checked (informational, N):` / `not checked (info)`, the summary adds `N not checked (info)`, and it neither fails the report nor `-ff`. `RepairRollups` (dry run and applied) prints the rollups it skipped under `Skipped, not repaired because they could not be checked:`.
- Output goes through `RollupConsistencyFormatter` (only report fields are printed). Tests: `RollupConsistencyCommandTests`.

## Dump through the operation handle; GetOperation / ListOperations (AB#8446)

`Dump` (`Asset/Tenants/DumpTenant.cs`) runs, for the tenant of the context, the asynchronous CK method
`System.Bot/TenantRepository.Dump-1` (System.Bot 3.7.0, bot services AB#8445) through `TenantRepositoryDumpClient` and the
SDK's `CkOperationsClient` (octo-sdk AB#8446, `docs/method-clients.md` "Asynchronous methods"):

- **Flow**: authenticate the tenant client (it has its own token), call `DumpAsync` (handle), log
  `Dump operation '<id>' accepted`, `WaitForOperationAsync` with `OperationProgressLogger` (one line per change: state,
  stage, percent, `jobId`), refresh the token before every read (`BeforeReadAsync`), download the result's `JobId` with
  `IBotServicesClient.DownloadDumpToFileAsync` as before. The wait honours `retryAfterSeconds` (1-30 s), no `Thread.Sleep`.
- **Fallback to the job API** (`CkMethodFallback`, shared with the user role commands since AB#8838): only when the call
  was certainly not executed — every GraphQL error carries `extensions.number` (document rejected by validation: asset
  repository without AB#8444 or tenant without System.Bot 3.7.0) or the code is `METHOD_HANDLER_NOT_AVAILABLE`; and always for `-tid` other than the context's
  tenant (the method runs only with a token of its own tenant). `METHOD_FORBIDDEN` and other refusals are thrown (Runner,
  exit -3), no fallback. A failed operation (`DUMP_FAILED`, `DUMP_INTERRUPTED`: `ServiceClientException` with the code, a
  readable text and the parameters; `METHOD_OUTCOME_UNKNOWN`: `CkMethodCallException`) downloads nothing.
- **`TenantRepositoryDumpClient`** has exactly the shape the SDK generator emits for the method (`DumpDocument` is pinned
  by `DumpMethodCommandTests` to the generator's text); keep it in step with the generator or replace it by the generated
  `RtTenantRepositoryMethodClient` of GraphQlDtos.
- **`GetOperation -id <operationId> [-w] [-j]`** and **`ListOperations [-m <methodId>] [-s <state>] [--since <time>] [-a]
  [-sk <n>] [-t <n>] [-j]`** (`Asset/Operations/`) read `runtime.operations` of the tenant of the context; text output via
  `OperationText`. Both are in `MachineReadableOutput.CommandNames`: standard output carries only the answer, logs (incl.
  the `-w` progress) go to standard error. `-w` waits until final (exit -3 when FAILED). `Runner` maps `CkOperationQueryException`
  (`OPERATION_NOT_FOUND`, `OPERATION_QUERY_REFUSED`) to one error line, exit -3.
- Tests: `DumpMethodCommandTests` (real SDK `TenantClient` against a local listener with answers recorded on test-2;
  every sent document validated against `Fixtures/asset-repo-schema.test-2-system-bot-3.7.json.gz` with
  `AssetRepositoryGraphQlSchema.ValidateAgainstSystemBotAsync`).

## Scratch tenants: an agent works in and cleans up what it creates (AB#7192)

An agent that creates throw-away child tenants with a client-credentials client no longer needs a human device-code login per tenant.

- **`Create --scratch` (`-sc`)** creates the tenant as a scratch tenant (`scratch=true` to the asset repository, `ScratchTenantMarker` in the system tenant
  configuration): the creating client's **mirror** in the child becomes administrator (`TenantOwners`). The current user is not provisioned (a service
  token has no user). The service accepts it only for a client-credentials token of the direct parent with `TenantManagement`; the client must be
  flagged `AutoProvisionInChildTenants` (`AddClientCredentialsClient -apic`), or it has no mirror there and gets nothing. **There is no `Restore --scratch`**: restore into a tenant created with `Create --scratch` using the child (mirror) token - the scratch record lives
  outside the tenant database and survives the restore; a plain restore writes no scratch record. `GetTenants` shows `isScratch`, `scratchCreatorClientId`,
  `scratchCreatedAtUtc`, `scratchAccessRevoked`.
- **Working in the child:** a context for the child tenant plus `LogInClientCredentials` with `OCTO_CLI_CLIENT_ID` / `OCTO_CLI_CLIENT_SECRET` of the
  mirror (its own secret from `RotateMirrorSecret`, or - while AB#5061 keeps the inherited secret - the parent client's secret) gives the usual
  child token; all existing verbs and the MCP tools then work in the tenant.
- **`Delete --disable-features` (`-df`, requires `-y`)** switches off every capability the tenant has enabled - AI Services, Communication, Reporting,
  Stream Data, in that order - with *the caller's token for that child tenant*, prints what it disabled, then deletes the tenant. The capability
  endpoints belong to the tenant's own administrator (a parent token is refused there), so `ChildTenantAccess` gets the child token: a
  client-credentials session with `OCTO_CLI_CLIENT_ID` and `OCTO_CLI_CHILD_CLIENT_SECRET` (the mirror's own secret; else `OCTO_CLI_CLIENT_SECRET`, the inherited parent
  secret, which stops working once the child no longer accepts it) requests `client_credentials` with `acr_values=tenant:<child>`
  (`IAuthenticatorClient.RequestClientCredentialsTokenForTenantAsync`); a user session exchanges its token (RFC 8693). The context's own token is never
  the fallback. It never deletes archives: when Stream Data stays enabled because archives are activated, the command lists them, does **not**
  delete the tenant and fails (`DisableArchive` keeps the data, `DeleteArchive` removes the archive). The orchestration is the SDK's
  `TenantFeatureDisabler`, shared with the MCP tool `delete_tenant` (`disableFeatures`).
- Tests: `ScratchTenantCommandTests` (Create flag, Delete with and without `--disable-features`, order, archives, child token for service and user sessions).
