---
description: Large uploads, restore outcome, rollup validate and repair, dump through the operation handle, scratch tenants.
applies_to: **/Asset/{Tenants,TimeSeries,Operations}/**, ChildTenantAccess.cs
---

# Tenant data operations

## Large uploads: Restore and ImportArchiveData

`Restore` (`Asset/Tenants/RestoreTenant.cs`) and `ImportArchiveData` (`Asset/TimeSeries/ImportArchiveData.cs`) upload through
the SDK's `RestoreRepositoryWithTusAsync` / `StartImportArchiveDataWithTusAsync`, which send the file in parts of 50 MiB by default
(up to 90 MiB with `--part-size-mib`; SDK `BotServiceClientOptions.TusPartSizeBytes`), so a backup of several GB passes the ingress body limit (100 MiB on
the shared environments; before, one PATCH of the whole file was refused with 413 and the command failed later on an
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

## Restore outcome

`Restore -w` reads the finished job (`JobDto`: `Outcome`, `FailedArchives`, `NotCheckedRollups`, derived `HasWarnings`) and does not print a
plain "restored" when the restore only partly succeeded. `RestoreOutcome.ThrowIfWarnings` logs every failed archive and every blocking
not-checked rollup as an error and ends with a `ToolException` (exit -5): `Tenant 'x' (database 'y') restored with N failed archive(s): <ids>;
M rollup(s) not checked: <id> [Cause]. The tenant is not fully restored.` (`restored with warnings` when only rollups are blocking).
Informational not-checked rollups (never activated) are logged as `not checked (info)` and do not change the result. A bot without these
fields (older bot, no `-w`) behaves as before. Tests: `RestoreOutcomeTests`; `RestoreOutcomeContractTests` feeds the wire JSON captured in the bot tests (failed archive, clean, informational) through the real `BotServicesClient` into the command.

## Rollup validate and repair

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

## Dump through the operation handle; GetOperation / ListOperations

`Dump` (`Asset/Tenants/DumpTenant.cs`) runs, for the tenant of the context, the asynchronous CK method
`System.Bot/TenantRepository.Dump-1` (System.Bot 3.7.0, bot services) through `TenantRepositoryDumpClient` and the
SDK's `CkOperationsClient` (`octo-sdk/docs/method-clients.md`, "Asynchronous methods"):

- **Flow**: authenticate the tenant client (it has its own token), call `DumpAsync` (handle), log
  `Dump operation '<id>' accepted`, `WaitForOperationAsync` with `OperationProgressLogger` (one line per change: state,
  stage, percent, `jobId`), refresh the token before every read (`BeforeReadAsync`), download the result's `JobId` with
  `IBotServicesClient.DownloadDumpToFileAsync` as before. The wait honours `retryAfterSeconds` (1-30 s), no `Thread.Sleep`.
- **Fallback to the job API** (`CkMethodFallback`, shared with the user role commands): only when the call
  was certainly not executed — every GraphQL error carries `extensions.number` (document rejected by validation: asset
  repository without the Dump support or tenant without System.Bot 3.7.0) or the code is `METHOD_HANDLER_NOT_AVAILABLE`; and always for `-tid` other than the context's
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
- Tests: `DumpMethodCommandTests` (real SDK `TenantClient` against a local listener with answers recorded on a shared test environment;
  every sent document validated against `Fixtures/asset-repo-schema.test-2-system-bot-3.7.json.gz` with
  `AssetRepositoryGraphQlSchema.ValidateAgainstSystemBotAsync`).

## Create with a blueprint

`Create --blueprint <Name>-<version>` (`-b`) sends `blueprintId` with the create call
(`IAssetServicesClient.CreateTenantAsync(child, db, scratch, blueprintId)`). The asset repository creates the tenant and applies
the catalog blueprint in one transaction: when the blueprint is unknown or fails, it aborts, leaves no tenant behind and answers
400 ("Blueprint application failed: ..."), which `Runner` prints as one error line with exit -3; the current user is then not
provisioned. Without `--blueprint` the command makes exactly the calls it made before (the SDK sends no `blueprintId`
parameter). It combines with `--database`, `--scratch` and `--no-provision`. Tests: `CreateTenantBlueprintCommandTests`
(argument wiring, order create-then-provision, failure path through the real SDK client against a local listener and `Runner`).

### Starter tenant: default LLM cap (AB#8452)

When the context's tenant is the system tenant (`octosystem`, or `system`, case ignored) and the blueprint is
`System.Ai.Starter-<version>`, `Create` then calls `IAiServicesClient.ApplyStarterDefaultSpendCapAsync(child)`
(AI service, `POST system/v1/aiSpendCaps/<child>/starterDefault`), as MCP `create_tenant starter=true` does. The order is
create (with the blueprint), cap, then the provisioning of the current user. The cluster configuration decides the amount
(`StarterSpendCap:DefaultTenantSpendCapEur`); the outcome is logged: `created` (key source and EUR per month), `exists`
(an existing cap is never changed) or `notConfigured` (warning, nothing written). Rules:

- Other blueprints, other parent tenants and `Create` without `--blueprint` make no AI call.
- The endpoint takes only a person login of the system tenant with `TenantManagement`. In a client-credentials session
  (no refresh token, e.g. `--scratch`) the command does not call it and warns, naming `SetAiSpendCap`.
- With a person login the context needs the AI service URL; without it the command stops before the tenant is created. A
  client-credentials session makes no AI call and needs none.
- When the cap call fails, the tenant exists: the error names `SetAiSpendCap -t <child> -c <EUR> -ks CentralKey -r <reason>`
  (and `ProvisionCurrentUser -ttid <child>`, which then did not run) and the command keeps the call's exit code.

Tests: `CreateTenantStarterCapCommandTests`.

The same rules apply to `InstallBlueprint System.Ai.Starter-<version> --child-tenant-id <child>` from a context of the
system tenant: after a successful install into the child, the command calls `starterDefault` for the child (the
endpoint creates the cap once, so a repeated install changes nothing). Until a starter tenant can be created with its
blueprint in one call (AB#10065: the System.Communication model reaches a new tenant only after the create commits), the
CLI path of a starter tenant is `Create -tid <child>` followed by this install. `--child-tenant-id` checks that the tenant
is a descendant of the context's tenant (`ChildTenantCheck`, shared with the client role commands) and installs with the
caller's token for the child (`IChildTenantAccess`: a user session exchanges the context's token, a client-credentials
session uses the client's mirror there). An install without `--child-tenant-id`, into a child of another parent or of
another blueprint sets no cap. Tests: `InstallBlueprintStarterCapCommandTests`.

## Scratch tenants: an agent works in and cleans up what it creates

An agent that creates throw-away child tenants with a client-credentials client no longer needs a human device-code login per tenant.

- **`Create --scratch` (`-sc`)** creates the tenant as a scratch tenant (`scratch=true` to the asset repository, `ScratchTenantMarker` in the system tenant
  configuration): the creating client's **mirror** in the child becomes administrator (`TenantOwners`). The current user is not provisioned (a service
  token has no user). The service accepts it only for a client-credentials token of the direct parent with `TenantManagement`; the client must be
  flagged `AutoProvisionInChildTenants` (`AddClientCredentialsClient -apic`), or it has no mirror there and gets nothing. **There is no `Restore --scratch`**: restore into a tenant created with `Create --scratch` using the child (mirror) token - the scratch record lives
  outside the tenant database and survives the restore; a plain restore writes no scratch record. `GetTenants` shows `isScratch`, `scratchCreatorClientId`,
  `scratchCreatedAtUtc`, `scratchAccessRevoked`.
- **Working in the child:** a context for the child tenant plus `LogInClientCredentials` with `OCTO_CLI_CLIENT_ID` / `OCTO_CLI_CLIENT_SECRET` of the
  mirror (its own secret from `RotateMirrorSecret`, or - while the child still accepts the inherited parent secret - the parent client's secret) gives the usual
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
