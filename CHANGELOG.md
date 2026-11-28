# Changelog

All notable changes to octo-cli are listed here. Newest first.

## Unreleased

### Added

- **AB#8452 — a starter tenant created from the system tenant gets its default LLM cap.** `Create --blueprint
  System.Ai.Starter-<version>` with a person login of the system tenant then calls the AI service's `starterDefault`
  endpoint for the new tenant (the cluster's default monthly cap on the central key, created once, never changed), as MCP
  `create_tenant starter=true` does. Other blueprints and other parent tenants make no AI call; a client-credentials session
  skips the call with a warning that names `SetAiSpendCap`. With a person login and without an AI service URL in the
  context the command stops before the tenant is created (a client-credentials session needs none); a failed cap call names `SetAiSpendCap` as the remedy. **Requires octo-sdk with
  `IAiServicesClient.ApplyStarterDefaultSpendCapAsync` and octo-ai-services with the endpoint (both AB#8452).**
- **AB#8452 — `InstallBlueprint --child-tenant-id` installs into a child tenant, and the starter gets its cap there too.**
  `InstallBlueprint -b <blueprint> -ctid <child>` installs into a child tenant (at any depth) of the context's tenant with
  the caller's token for that child (the token path of `Delete --disable-features`). From the system tenant with
  `System.Ai.Starter-<version>` a successful install then sets the child's default LLM cap (`starterDefault`) with the rules
  of `Create --blueprint`: person login only (a client-credentials session skips the call with a warning naming
  `SetAiSpendCap`), an existing cap is never changed, a person login without an AI service URL stops before the install.
  This is the second call of the starter path until a starter can be created with its blueprint in one call (AB#10065).
- **AB#9060 — archive commands run the System.StreamData methods.** `ActivateArchive`, `DisableArchive`, `EnableArchive`,
  `RetryArchiveActivation`, `DeleteArchive`, `AddArchiveColumn`, `AddComputedColumn`, `RemoveComputedColumn`,
  `UpdateComputedColumnFormula`, `RetryComputedColumnBackfill`, `FreezeRollupArchive`, `UnfreezeRollupArchive`,
  `RewindRollupWatermark`, `RecomputeArchive` and `BackfillRollup` call the CK methods of `System.StreamData` (generated from
  `System.StreamData-[1.30,2.0)`) through the asset repository instead of the REST endpoints; names, arguments and output are
  unchanged. The lifecycle and column methods run through the client of the archive's concrete type (one GraphQL read of the
  archive's type first). `RecomputeArchive` and `BackfillRollup` follow the operation of `Recompute-2` / `BackfillFromSource-2`
  by its handle; a recompute job that ended Failed prints its job line and exits with 0, as before. A refused call prints the
  declared code, message and parameters (exit -3). **Requires an asset repository whose tenant has System.StreamData 1.30.0**
  (no REST fallback) and the role `StreamDataAdmin`, as the REST endpoints did. The reads, the rollup check/repair, the stream
  data switches and the archive data export/import stay REST.
- **AB#8455 — Tenant administrators manage agents with octo-cli.** `CreateAgent`, `UpdateAgent`, `EnableAgent`,
  `DisableAgent` and `GetAgents` (list, or one agent with `-a`) call the agent tools of the MCP service (`create_agent`,
  `update_agent`, `set_agent_enabled`, `list_agents`, `get_agent`) on `<McpServiceUrl of the context>/<tenant>/mcp` with
  the signed-in user's token, so the checks stay in the MCP service (the write commands need a tenant administrator and
  refuse an agent's own token; a refusal ends with the tool's code, e.g. `NotTenantAdministrator`). No per-call URL option and no client-secret option; the token is never
  logged or printed. A refusal of `create_agent` after a write no longer claims that nothing changed. Listing agent runs
  is not part of this change (it needs the run orchestrator). **Requires octo-mcp-service with the
  agent tools (AB#8454) and the context field `McpServiceUrl` (AB#9647).**
- **AB#9872 — client role commands reach a child tenant.** `GetClientRoles`, `AddClientToRole` and `RemoveClientFromRole` take
  the optional `-ctid|--child-tenant-id <tenant>` (the option name of `ProvisionClientInTenant`): they act in that child tenant
  (at any depth) of the context's tenant with the caller's token for it, obtained like `Delete --disable-features` does (token
  exchange of a signed-in user, or the client's mirror in a client-credentials session), so a role grant on every tenant of a
  cluster needs only the parent context. A tenant that is not listed by `GetTenants -r` of the context, the context's own
  tenant and a caller without a token for the child end with a message naming the tenant and exit code -5; a refusal in the
  child keeps exit code -3. No token is printed. Same options and output as before; without the option nothing changes.
  Needs no new server version.
- **AB#9699 — `GetClientRoles` lists the roles of a client in a tenant.** `octo-cli -c GetClientRoles -id <clientId>` prints
  the roles assigned directly to the client in the tenant of the context as JSON (`Id`, `Name`; group-inherited roles are not
  listed), read-only through the existing identity REST reads, so operators can check a grant such as `CommunicationManagement`
  for the CD deploy clients before and after a change; standard output carries only the JSON (logs go to standard error), so
  scripts can pipe it. An unknown client or tenant ends with a message naming it and exit
  code -5 (a context whose tenant does not exist usually stops earlier, at sign-in, with -4). Needs no new server version.
- **AB#8690 — `SetAiSpendCap` and `GetAiSpend` (AI services group).** `SetAiSpendCap -t <tenant> -c <EUR> -r <reason>
  [-ks <key source>]` sets the monthly LLM spend cap of a tenant for the central-key LLM gateway
  (`PUT system/v1/aiSpendCaps/{tenant}`); without `-ks` the tenant keeps its stored key source (`CentralKey` when it has none yet); only a context of the system tenant with the role `AiBudgetAdmin` succeeds,
  every change is audited by the AI service. A cap that is not a decimal number of 0 or more (with `.`), an unknown key
  source or a missing reason is refused before any request. `GetAiSpend` shows key source, cap and the spend (spent,
  reserved, calls, reset) of the current and previous UTC month: of the context's own tenant, or with `-t` of another
  tenant through the cap administration.

### Changed

- **AB#9059 — tests ready for the obsolete stream-data REST methods.** `AddArchiveColumnCommandTests` and
  `ComputedColumnBackfillCommandTests` assert that the REST methods `AddArchiveColumnAsync` / `AddComputedColumnAsync`
  of `IStreamDataServicesClient` are not called; that assertion now suppresses `CS0618` for its own lines, so the test
  build stays green when octo-sdk marks the 15 replaced archive methods `[Obsolete]` (AB#9059 part 2). No `src` change:
  the commands call the System.StreamData method clients since AB#9060.
- **AB#7970 — `Setup` creates the first administrator of a child tenant.** New `-tid/--tenantId` calls `POST {tenant}/v1/setup` with a client-credentials token issued for that tenant (the context's own when it was signed in on that tenant, else one requested with `OCTO_CLI_CLIENT_ID`/`OCTO_CLI_CLIENT_SECRET`, e.g. of the client that created the tenant). A signed-in user is refused and pointed to `Create` / `ProvisionCurrentUser` and `CreateUser`, because the token exchange would create a user in the tenant and the setup runs only while it has none. Without `-tid` (or with `-tid octosystem`) `Setup` keeps the former `system/v1/setup` call without a token, whatever the tenant of the context.
- **AB#9609 — NuGet dependency pass.** Updated NLog.Extensions.Logging 6.3.0, System.IdentityModel.Tokens.Jwt 8.23.0 and Microsoft.CodeAnalysis.CSharp 5.12.0 and the test packages (coverlet.collector 10.1.1, Microsoft.NET.Test.Sdk 18.12.0, xunit.v3.mtp-off 4.0.3, xunit.runner.visualstudio 4.0.1); no behaviour change.
- **AB#9053 — Communication write commands run the System.Communication methods.** `DeployPipeline`, `ExecutePipeline`,
  `SetPipelineDebug`, `MovePipelinesToAdapter`, `Deploy/UndeployDataFlow`, `Deploy/UndeployDeploymentSite`,
  `Deploy/UndeployTriggers`, `Deploy/UndeployWorkload`, `UpdateWorkloadChartVersion`, `CancelQueuedExecution` and
  `RotateAdapterServiceAccountSecret` call the CK methods (method clients generated from `System.Communication-[4.19,5.0)`)
  instead of the REST endpoints; names, arguments and output are unchanged, the reads stay REST. A refused call prints the
  code and message of the method error and exits with -3. **Requires communication controller services with System.Communication
  4.19.0; roll them out before this CLI version.** **Every one of these commands now needs the role `CommunicationManagement`**
  (REST accepted any `octo_api` caller): grant it to the users and client credentials clients that run them, including the
  CD deploy clients, before they use this version. There is no REST fallback: against an older communication controller the
  commands stop with a message naming the needed version.
- **AB#9045, AB#9046 — Identity administration commands run the System.Identity methods.** The write commands of groups,
  clients (create, update, scopes, delete, roles, overlays, mirrors), API secrets, roles, API scopes and resources, data
  permissions and policies, e-mail domain group rules, external tenant user mappings, admin provisioning and identity providers
  call the CK methods (method clients generated from `System.Identity-[3.8,4.0)`) instead of the REST endpoints; names,
  arguments and output are unchanged. A refused call prints the code and message of the method error and exits with -3. A new
  secret is printed once and never logged. **Requires octo-identity-services with the wave 2 methods (System.Identity 3.8.0); roll
  out identity before this CLI version.** There is no REST fallback: against an older identity service the commands stop with a
  message naming the needed version (and, for the secret-carrying commands, `Methods:Identity:EnablePasswordMethods`).

### Fixed

- **AB#9266 — The help of the API secret commands and `AddClientCredentialsClient` names both role settings.** It said
  `TenantManagement` or `UserManagement` were only enough while `SecretManagementRoleEnforcement=Warn`; the identity service
  also lets them through while `RoleEnforcement=Warn` and refuses them only when `SecretManagementRoleEnforcement=Enforce` and
  `RoleEnforcement` is not `Warn`. Help text only, no behaviour change.

- **AB#9063 — `LogInClientCredentials` no longer replaces an interactive login without a word.** Context names are matched
  case-insensitively, so `--context local_octosystem` selected the stored context `local_OctoSystem` and its device-code login
  was overwritten by a client-credentials token. The command now refuses, names the matched context and changes nothing when the
  context holds an interactive login or the requested name matched the stored one only by case; `--replace-login` (`-rl`)
  replaces the login explicitly. Re-running the login on a client-credentials context with its exact name works as before. When a rejected refresh
  token is replaced through the client-credentials environment variables (AB#4754 fallback), octo-cli now warns and names the
  context instead of logging an info line.

- **AB#9103 — SetKnowledgePageStatus writes its version under the tenant key `<slug>@t<n>`.** `<slug>@<n>` belongs to the
  blueprint seed (decision Odin 2026-11-16, same rule as the knowledge tools of octo-mcp-service). Before, a status version
  could take the key of the blueprint's next version, and UpdateBlueprint then kept the tenant entity: the platform update
  was lost without a PlatformUpdateAvailable finding. The number continues the page's one sequence (history query
  `^<slug>@t?[0-9]+$`); existing plain tenant keys are counted as before.

### Added

- **AB#9647 - `ListKnowledgePlatformUpdates` and `AdoptKnowledgePlatformUpdates`.** Operators adopt newer Blueprint versions of unedited knowledge pages right away instead of waiting for the AI service's 15-minute pass, also where agents have no MCP client. Both commands call the MCP tools `knowledge_list_platform_updates` / `knowledge_adopt_platform_updates` on `<MCP service URI>/<tenant>/mcp` with the context's own token (exchanged for another tenant like every command), so the rules are the tool's: edited pages are never changed. `AdoptKnowledgePlatformUpdates --dryRun` only lists; `--runRef` is stored as `AdoptedByRun` (default `octo-cli:<UTC time>`). New context field `McpServiceUrl` (`AddContext -msu`, `Config -msu`, shown by `ListContexts`; https, http only for localhost); the commands take no per-call URL, so the token goes only to the MCP service of the context. A run that fails part-way prints the pages it already switched.

- **AB#9577 — `Create --blueprint` creates a tenant with a catalog blueprint applied in one call.** `--blueprint <Name>-<version>`
  (`-b`) sends `blueprintId` with the create; the asset repository applies it in the create transaction, so an unknown or failing
  blueprint ends the command with exit -3 and the service's message and leaves no tenant behind (the user is not provisioned).
  Combines with `--database`, `--scratch` and `--no-provision`; without it the calls are unchanged. Needs octo-sdk with the
  `CreateTenantAsync(…, blueprintId)` overload.

- **AB#8966 — `UninstallBlueprint --dry-run` and the kept entities.** `--dry-run` (`-dr`) lists the entities an uninstall would
  erase and the tenant-owned seed entities it would keep, with the reason (`Edited`, `Referenced` with the referencing rtIds,
  `NotComparable`, `EraseFailed`), without changing anything and without a prompt. It calls the asset repository's
  `POST blueprints/{name}/uninstall/preview`; an asset repository without AB#8966 answers an empty 404 and nothing happens ("not installed" is a 404
  with a message). With the new SDK a blocked uninstall (409) prints the `--cascade` hint again instead of an exception. A failed or blocked
  uninstall (or dry run) now ends with a non-zero exit code after printing the result. Both modes
  log one line per kept entity; the JSON output carries `DryRun`, `DeletedEntities` and `TenantOwnedKept`. The confirmation
  prompt now says that unedited tenant-owned seed entities are erased too.

### Changed

- **AB#9010 — Help never shows a password as an argument.** `Setup` gets help samples (hidden prompt and
  `--password-stdin`) and a note; the notes of `Setup`, `CreateUser` and `ResetPassword` name `-p/--password` as deprecated,
  removal date to be announced (AB#8861). Help text only, no behaviour change.

- **AB#8418 — CreateUser and ResetPassword run the User password methods.** Both commands now call the CK methods
  `System.Identity/User.CreateUser-1` / `User.ResetPassword-1` through GraphQL instead of the REST endpoints
  `POST users` / `POST users/ResetPassword`. Names, options, the confirmation prompt and the output are unchanged; the
  password is read from the hidden prompt, `--password-stdin` or `OCTO_CLI_NEW_PASSWORD` (`-p` keeps its deprecation
  warning) and travels only in the request body. Refusals are decided by the server and printed as code and message with
  exit code -3 (`USER_ALREADY_EXISTS`, `USER_NAME_RESERVED`, `PASSWORD_POLICY_VIOLATION` followed by the codes of the failed rules in brackets,
  `SHADOW_USER_PASSWORD_NOT_ALLOWED`, `METHOD_FORBIDDEN`, `METHOD_TARGET_NOT_FOUND`). **Needs** identity services 3.5.57
  (CreateUser) / 3.5.52 (ResetPassword) or newer with `Methods:Identity:EnablePasswordMethods` on; against an older
  service the command stops with a message naming that version - there is no REST fallback. This replaces the AB#8839
  detection of identity services before 3.5.55. Tests prove with a canary password that it appears in no log line (Trace
  included), no console output and no request URL.

### Added

- **AB#8778 — Tenant-owned seed refresh in the update output.** `PreviewBlueprintUpdate` and `UpdateBlueprint` (also
  `--dry-run`) log two more tenant-owned tables: the unedited entities that receive the changed seed
  (`tenant-owned, refreshed`) and the ones the update keeps although the seed changed because they differ from the installed
  seed (`tenant-owned, update available`). Information only; `--failOnBlanking` ignores them. Needs octo-sdk and asset repository with AB#8778.

- **AB#8446 — Dump follows the operation handle; GetOperation and ListOperations.** `Dump` for the tenant of the
  context runs the asynchronous CK method `System.Bot/TenantRepository.Dump-1` (bot services AB#8445): it prints the
  operation id and the progress (state, stage, percent, job id) read from `runtime.operations`, honours the service's
  `retryAfterSeconds` instead of polling every 2 seconds, refreshes the token before every read and downloads the job of
  the result as before. A declared error (`DUMP_FAILED`, `DUMP_INTERRUPTED`) and a lost handler
  (`METHOD_OUTCOME_UNKNOWN`, `reason=handler-lost`) end the command with exit -3 and nothing is downloaded. It uses the
  job API unchanged when the asset repository rejects the method document (no System.Bot 3.7.0 / no operation handles)
  or no handler is live (`METHOD_HANDLER_NOT_AVAILABLE`), and always for a tenant other than the context's. New commands
  `GetOperation -id <operationId> [-w] [-j]` and `ListOperations [-m] [-s] [--since] [-a] [--skip] [-t] [-j]` read the
  operations of asynchronous CK methods; `OPERATION_NOT_FOUND` / `OPERATION_QUERY_REFUSED` exit -3. Their standard output
  carries only the answer (logs on standard error), so `-j` can be piped. Needs the octo-sdk
  package with `CkOperationsClient` (octo-sdk AB#8446).

### Fixed

- **AB#8968 — SetKnowledgePageStatus keeps the version chain of a knowledge page.** The status version takes the next free
  number (above the current version and every existing version, e.g. a seeded Blueprint version not adopted yet) instead
  of `CurrentVersion + 1`, so a version name is never written twice, and records the replaced version as `BaseVersion`
  (System.Ai 4.11.0 or newer). A verify no longer cuts the ancestor chain of the platform-update rule (AB#8780): Blueprint
  versions the person already saw are not reported as `PlatformUpdateAvailable` again. A verified page counts as edited:
  a newer Blueprint version is reported, never adopted automatically.

- **AB#8838 — AddUserToRole / RemoveUserFromRole work against released 3.5.x servers again.** Since AB#8417 the commands
  run the CK methods `System.Identity/User.AddRole-1` / `RemoveRole-1`, which exist only from asset repository 3.5.43 and
  identity 3.5.52 on. An older server (e.g. OctoMesh 3.5.23) rejected the call with
  `The CK method call 'System.Identity/User.AddRole-1' failed: VARIABLES_ARE_INPUT_TYPES` and the role was not assigned.
  When the server's schema lacks the method (GraphQL validation error) or no handler is live, the commands now log one
  line and use the REST endpoint of those servers; other refusals are reported as before (exit -3). `AddUserToRole` with
  an unknown role now exits with -3 instead of 0. `AddClientToRole` / `RemoveClientFromRole` use REST and were not
  affected.
- **AB#8839 — ResetPassword against an identity service before 3.5.55 says what is wrong.** Since AB#8008 the new
  password travels only in the request body; older identity services accept it only in the URL and answered
  `BadRequest: … "password":["The password field is required."],"userName":["The userName field is required."]`, which
  read as if the CLI had sent neither. The command now recognizes that answer and exits with -3 and one message: the
  identity service is older than 3.5.55, the password was not changed, update the identity service or let the user reset
  the password with "Forgot password" on the sign-in page. Blank values keep the original validation errors (a current
  identity reports them as missing too). octo-cli still never sends the password in the URL.
- **AB#8570 — no credentials in error output.** A failed server call no longer prints the request's
  `Authorization` header, cookies, bearer tokens or credential fields of a request body. A service with the
  ASP.NET Core developer exception page (the local stack) answers a 500 with a page that echoes the request
  headers; that body reached the console and the log file as part of the error message. Every log line (console,
  log file, every level, exception texts included) and the GraphQL error body on standard error now pass through
  one central redaction (`ErrorOutputRedactor`, wired into the NLog layouts by `RedactedLogOutput`). Status and
  the server's error message stay visible; the developer page's header section is replaced by
  `[request headers removed by octo-cli]`, credential values by `[REDACTED]`. A custom `nlog.config` with a
  lower log level is redacted the same way.
