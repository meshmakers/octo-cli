---
description: Session revocation, sibling-tenant grants, user password and role methods, the 403 message with the missing role.
applies_to: src/ManagementTool/Commands/Implementations/Identity/**, src/ManagementTool/{Runner,ForbiddenResponseMessage,CkMethodErrorText,CkMethodFallback}.cs
---

# Identity commands

## Revoke sessions: RevokeUserSessions / RevokeShadowUserSessions (AB#7899)

Identity commands on the tenant of the context (identity PR #210, SDK `IIdentityServicesClient.RevokeUserSessions` /
`RevokeShadowUserSessions`):

- `octo-cli -c RevokeUserSessions -un <userName> [-y]` — `POST {tenant}/v1/users/{userName}/revokeSessions`: rotates the
  security stamp, revokes refresh tokens and authorizations, removes the browser sessions. Asks for confirmation unless `-y`.
- `octo-cli -c RevokeShadowUserSessions [-a|--apply] [-y]` — the same for every cross-tenant shadow user (`xt_*`). **Dry run
  without `--apply`** (`dryRun=true`: counts and the first 100 user names, nothing changes, no question asked); with `--apply`
  it asks for confirmation (unless `-y`) and revokes.
- Needs `UserManagement` in that tenant. One tenant per call: for the rollout run it once per tenant with a context of that
  tenant. Issued access tokens stay valid until they expire.
- Tests: `RevokeSessionsCommandTests`.

## Sibling-tenant grants: LookupSourceTenantUser, groups, allow-list (AB#8346)

Identity commands of Feature AB#8313 (identity AB#8341 lookup, AB#8343 groups, AB#8345 allow-list; contract
`docs/CONCEPT-SIBLING-TENANT-GRANTS.md` in octo-identity-services). The tenant of the context is the **target** tenant T
for the grant commands and the **source** tenant S for the allow-list commands.

- `octo-cli -c LookupSourceTenantUser -st <S> -id <exact user name or e-mail>` — `POST {T}/v1/externalTenantUserMappings/sourceUserLookup`.
  Prints `SourceTenantId`, `UserId`, `UserName`, `DisplayName` (no e-mail). Every negative case is the one message
  `No user found that can be granted access (code SourceUserNotFound).`, exit `-3`. The identifier is **never logged**
  (only the service's answer is printed); tests assert it for hit, miss and 429.
- `octo-cli -c CreateExternalTenantUserMapping -stid <S> -suid <userId> -sun <userName> [-rids …] [-g <groups>]` — `-g/--groups`:
  comma-separated group IDs (24 hex digits) or group names of T (names resolved with `GetGroupByName`; an unknown name fails
  with exit `-5` before anything is written). Uses the SDK `GrantExternalTenantUser` and prints the mapping **as stored**
  (user name from S's user record, `GroupNames`). `-sun` stays required (API contract) although the service ignores it.
  Existing parameters unchanged. `GetExternalTenantUserMappings` already prints `SourceTenantId` and `GroupNames`.
- `octo-cli -c GetCrossTenantGrantPermissions` / `octo-cli -c SetCrossTenantGrantPermissions (-ttids a,b | --none)` —
  S's allow-list (`GET`/`PUT {S}/v1/crossTenantGrantPermissions`). Set reads the list first and logs `added […]`,
  `removed […]`; exactly one of `--targetTenantIds`/`--none` (else exit `-5`, no call). Only S administrators
  (UserManagement + TenantManagement, token for S) — anyone else gets 403. The SDK `UpdateCrossTenantGrantPermissions` has an
  optional `ifMatch` reserved for AB#8432 (ETag); the command does not send it yet.
- **Problem answers** (`Runner`): an RFC 9457 body **with a `code`** is printed as one line via SDK `ServiceProblem.ToDisplayText()`
  — e.g. `Source tenant not permitted (HTTP 403, code SourceTenantNotPermitted)`, `… (HTTP 429, code SourceUserLookupRateLimited). Retry after 240 s`,
  `… Unknown group ids: …`, `… Existing mapping id: …` — exit `-3`, and **without** the ProvisionCurrentUser hint of a bare 403.
  Problems without a code (validation problems, `{"message": …}`, empty 403) keep the earlier paths.
- Tests: `CrossTenantGrantCommandTests` (real commands + real SDK client against a local listener, through the real `Runner`).

## CreateUser and ResetPassword run the User password methods (AB#8418)

`CreateUser` and `ResetPassword` (`Identity/Users/`) run the CK methods `System.Identity/User.CreateUser-1` and
`User.ResetPassword-1` through the generated `RtUserMethodClient` (GraphQlDtos, `System.Identity-[3.2,4.0)`), via
`UserPasswordMethodCall`. Command names, arguments, the confirmation prompt of `ResetPassword` and the log output are unchanged; the
password sources stay those of AB#8044 (hidden prompt, `--password-stdin`, `OCTO_CLI_NEW_PASSWORD`; `-p` deprecated with a warning).

- **Flow**: `CreateUser` calls `CreateUserAsync(userName, email, password)` (no target; password optional). `ResetPassword` looks the
  user up by name (`GetUserByNameEmailOrId`, `UserDto.UserId`; an unknown user - also a user of another tenant - is
  `User with name '<name>' not found.`, exit -3, no method call) and calls `ResetPasswordAsync(rtId, password)`. Both authenticate the
  tenant client first (own access token). The password travels only in the GraphQL request body (`variables.input.password` /
  `.newPassword`).
- **No REST fallback** (decision AB#7039; the AB#8838 role fallback is deliberately not copied). When the asset repository rejects the
  document (GraphQL validation, `CkMethodFallback.IsMethodUnavailable`) or answers `METHOD_HANDLER_NOT_AVAILABLE` (identity too old or
  `Methods:Identity:EnablePasswordMethods=false`), the command throws a `ServiceClientException` (exit -3) that names the needed identity
  version (`UserPasswordMethodCall.CreateUserMinimumIdentityVersion` 3.5.57, `ResetPasswordMinimumIdentityVersion` 3.5.52 - the
  releases that added the handlers), the switch, that nothing was changed and that octo-cli does not fall back to REST.
- **Refusals are the server's**: a declared error (`USER_NAME_RESERVED`, `USER_ALREADY_EXISTS`, `PASSWORD_POLICY_VIOLATION`,
  `SHADOW_USER_PASSWORD_NOT_ALLOWED`) becomes `CODE: message (parameters) [detail codes]` (`CkMethodErrorText`; the asset repository sends the failed password
  rules only as detail codes, e.g. `[PasswordTooShort, PasswordRequiresUpper]` — found in the E2E run; the CLI keeps no
  copy of the password policy). An undeclared refusal (`METHOD_FORBIDDEN` with `reason=role-required` / `tenant-not-allowed`,
  `METHOD_TARGET_NOT_FOUND`, …) is rethrown as a `ServiceClientException` with the same text the Runner prints for a
  `CkMethodCallException`. A 403 of the asset repository (token of another tenant) is the SDK's `Call to GraphQL source failed.` All exit -3.
- **The password never reaches output**: every text `UserPasswordMethodCall` produces passes `Scrub` (the password replaced by
  `[REDACTED]`, defence in depth against a server that would echo it), and undeclared refusals are rethrown without the inner exception.
  On top, `RedactedLogOutput` applies as for every command.
- Tests: `UserPasswordMethodCommandTests` (fake `ITenantClient`, real `Runner`: method document and variables, no REST call, unchanged
  log lines, one test per declared error, `METHOD_FORBIDDEN` role/tenant, `METHOD_TARGET_NOT_FOUND`, HTTP 403, unknown user, server
  without the method → version message, echoed password redacted); `UserPasswordCanaryTests` (real commands, real SDK clients and real
  `Runner` against a local identity + GraphQL listener, NLog at Trace with the `nlog.config` layout, stdout/stderr captured: a canary
  password appears only in the GraphQL request body - in no log line, no console output and no URL - for success, every declared error,
  refusals, a missing method, HTTP 403 and the deprecated `-p`); `SecretArgumentCommandTests` (the three input sources, now read from the
  method call's variables).

## User role commands run the User role methods (AB#8417)

`AddUserToRole` and `RemoveUserFromRole` (`Identity/Users/`) run the CK methods `System.Identity/User.AddRole-1` and
`User.RemoveRole-1` through `RtUserMethodClient`, which the source generator emits in **GraphQlDtos** from
`System.Identity-[3.2,4.0)` (`src/GraphQlDtos/construction-kits.yaml`, namespace `GraphQlDtos.DataTransferObjects.System.Identity.v3`;
pattern of the SDK migration guide, octo-sdk `docs/method-client-migration-guide.md`, "Identity wave 1"). The REST methods
`IIdentityServicesClient.AddRoleToUser` / `RemoveRoleFromUser` are `[Obsolete]` since octo-sdk AB#8416; octo-cli calls them only as the
fallback below (`#pragma warning disable CS0618` at that one place). Command names, arguments, confirmation and log output are unchanged.

- **Flow** (`UserRoleMethodCall`): look the user up by name (`IIdentityServicesClient.GetUserByNameEmailOrId`, `UserDto.UserId` = runtime
  id), authenticate the tenant client (`ITenantClient` has its own access token; `PreValidate` only authenticates the identity client),
  call the method with the role **name** (the handler resolves it case-insensitively, like REST). `AddUserToRole` keeps its role
  existence check before the call; an unknown role is a `ServiceClientException` (exit -3; before AB#8838 it only logged and exited 0).
- **Older servers (AB#8838)**: the role methods exist only with asset repository 3.5.43 (AB#5670) and identity 3.5.52 (AB#7776). A released
  server before that (e.g. the FH lab installation on 3.5.23) rejects the document in GraphQL validation (`VARIABLES_ARE_INPUT_TYPES`,
  `KNOWN_TYPE_NAMES`, `FIELDS_ON_CORRECT_TYPE`, each with `extensions.number`). Then, and on `METHOD_HANDLER_NOT_AVAILABLE`, the call was
  certainly not executed: `UserRoleMethodCall` logs one information line (`The CK method … is not available (…); the role is changed
  through the REST endpoint of earlier identity versions`) and calls the REST method. The rule is `CkMethodFallback` (shared with `Dump`,
  AB#8446); any other refusal (`METHOD_FORBIDDEN`, declared errors) never falls back.
- **Refusals**: a declared error (`ROLE_NOT_FOUND`, `ROLE_ALREADY_ASSIGNED`, `ROLE_NOT_ASSIGNED`) becomes a `ServiceClientException` with
  the code and the message the REST endpoint answered before, e.g. `ROLE_ALREADY_ASSIGNED: User 'john.doe' already has role 'dataanalyst'`
  (Runner: error log, exit -3, as for a refused REST call). An undeclared refusal (`METHOD_FORBIDDEN` with `reason=self-grant` /
  `role-not-held` / `last-administrator`, `METHOD_TARGET_NOT_FOUND`) is the SDK's `CkMethodCallException`; `Runner` logs it with code and
  parameters (`CkMethodErrorText`), exit -3.
- `CreateUser` and `ResetPassword` run the password methods since AB#8418 (previous section), without a REST fallback.
- Tests: `UserRoleMethodCommandTests` (generated client over a fake `ITenantClient`: method document and rtId, no REST role call,
  unchanged log lines, declared and undeclared refusals, declined confirmation, unknown user, unknown role fails; AB#8838: the validation
  answer of a 3.5.23 server and `METHOD_HANDLER_NOT_AVAILABLE` fall back to REST, a REST refusal is thrown, a refusal never falls back).

## 403 names the missing role (AB#8094)

`Runner` answers a `403` (`ServiceClientResultException`, exit code `-3`) with the `message` of the identity API body
(`{"message": "...requires the role 'SecretManagement'..."}`, AB#5861) printed verbatim; `ForbiddenResponseMessage` reads it once from
the exception text (the SDK keeps the body there). A 403 without body or without a usable `message` (the cross-tenant case) keeps the
generic text with the `ProvisionCurrentUser` hint. The notes of the ApiSecret write commands and `AddClientCredentialsClient` state that
writes need the tenant role `SecretManagement` (`TenantManagement`/`UserManagement` only while the cluster runs `SecretManagementRoleEnforcement=Warn`);
the command reference page is generated from them. Tests: `ForbiddenRoleMessageTests`.
