---
description: First-admin setup of a tenant, session revocation, sibling-tenant grants, user password and role methods, GetClientRoles, client roles in a child tenant, the 403 message with the missing role.
applies_to: **/Implementations/Identity/**, {Runner,ForbiddenResponseMessage,CkMethodErrorText,CkMethodFallback}.cs
---

# Identity commands

## First administrator: Setup

`octo-cli -c Setup -e <email> [-tid <tenant>]` creates the first administrator of a tenant that has no user yet
(`SetupCommand`). Without `-tid/--tenantId` it is always the system tenant; the tenant of the context is **not** used, so the
documented first-install command `Setup -e …` keeps creating the system administrator from any context.

- **System tenant** (no `-tid`, or `-tid octosystem` / `system`): unchanged, SDK `IIdentityServicesSetupClient` on
  `POST system/v1/setup`, no token.
- **Any other tenant, only with an explicit `-tid`**: `POST {tenant}/v1/setup` through `ITenantSetupClientFactory` (the SDK client addresses only the
  system route), with a **client-credentials** token issued for that tenant: the context's token through
  `AuthenticationService` when the context was signed in with `LogInClientCredentials` on that tenant (its `tenant_id` is the
  target), else a token from `ChildTenantAccess` (client credentials of the tenant's creator or mirror,
  `OCTO_CLI_CLIENT_ID`/`OCTO_CLI_CLIENT_SECRET`). The token is obtained before the password is asked for; without one the
  command stops before the call.
- **A signed-in user is refused** for any other tenant, before any token request: the token exchange (RFC 8693) creates a
  shadow user in the target tenant, and the service accepts the setup only while the tenant has no user, so a user session can
  never create the first administrator (and would block the client path afterwards). The refusal names the operator path
  instead: `Create` (provisions the caller) or `ProvisionCurrentUser -ttid <tenant>`, then `CreateUser` in the child tenant.
  `Setup -tid` is for client-credentials sessions (scratch tenants, mirrored clients).
- An empty `-tid` and a malformed tenant id (not 1-64 letters, digits, `-`, `_`) are refused before any call.
- The service accepts the call only while the tenant has no user; the new administrator joins `TenantOwners`. Its `404` on a
  child tenant (users already configured, or no such tenant) becomes a message that names `CreateUser` and
  `ProvisionCurrentUser`; the system route passes the service's answer on unchanged.
- Tests: `SetupCommandTenantTests`, `SecretArgumentCommandTests.Setup_FromTheEnvironment`.

## Revoke sessions: RevokeUserSessions / RevokeShadowUserSessions

Identity commands on the tenant of the context (SDK `IIdentityServicesClient.RevokeUserSessions` /
`RevokeShadowUserSessions`):

- `octo-cli -c RevokeUserSessions -un <userName> [-y]` — `POST {tenant}/v1/users/{userName}/revokeSessions`: rotates the
  security stamp, revokes refresh tokens and authorizations, removes the browser sessions. Asks for confirmation unless `-y`.
- `octo-cli -c RevokeShadowUserSessions [-a|--apply] [-y]` — the same for every cross-tenant shadow user (`xt_*`). **Dry run
  without `--apply`** (`dryRun=true`: counts and the first 100 user names, nothing changes, no question asked); with `--apply`
  it asks for confirmation (unless `-y`) and revokes.
- Needs `UserManagement` in that tenant. One tenant per call: for the rollout run it once per tenant with a context of that
  tenant. Issued access tokens stay valid until they expire.
- Tests: `RevokeSessionsCommandTests`.

## Sibling-tenant grants: LookupSourceTenantUser, groups, allow-list

Identity commands for sibling-tenant grants (lookup, groups, allow-list; contract
`octo-identity-services/docs/CONCEPT-SIBLING-TENANT-GRANTS.md`). The tenant of the context is the **target** tenant T
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
  optional `ifMatch` reserved for a later ETag check; the command does not send it yet.
- **Problem answers** (`Runner`): an RFC 9457 body **with a `code`** is printed as one line via SDK `ServiceProblem.ToDisplayText()`
  — e.g. `Source tenant not permitted (HTTP 403, code SourceTenantNotPermitted)`, `… (HTTP 429, code SourceUserLookupRateLimited). Retry after 240 s`,
  `… Unknown group ids: …`, `… Existing mapping id: …` — exit `-3`, and **without** the ProvisionCurrentUser hint of a bare 403.
  Problems without a code (validation problems, `{"message": …}`, empty 403) keep the earlier paths.
- Tests: `CrossTenantGrantCommandTests` (real commands + real SDK client against a local listener, through the real `Runner`).

## CreateUser and ResetPassword run the User password methods

`CreateUser` and `ResetPassword` (`Identity/Users/`) run the CK methods `System.Identity/User.CreateUser-1` and
`User.ResetPassword-1` through the generated `RtUserMethodClient` (GraphQlDtos, `System.Identity-[3.2,4.0)`), via
`UserPasswordMethodCall`. Command names, arguments, the confirmation prompt of `ResetPassword` and the log output are unchanged; the
password sources stay those of the secret-input rules (hidden prompt, `--password-stdin`, `OCTO_CLI_NEW_PASSWORD`; `-p` deprecated with a warning).

- **Flow**: `CreateUser` calls `CreateUserAsync(userName, email, password)` (no target; password optional). `ResetPassword` looks the
  user up by name (`GetUserByNameEmailOrId`, `UserDto.UserId`; an unknown user - also a user of another tenant - is
  `User with name '<name>' not found.`, exit -3, no method call) and calls `ResetPasswordAsync(rtId, password)`. Both authenticate the
  tenant client first (own access token). The password travels only in the GraphQL request body (`variables.input.password` /
  `.newPassword`).
- **No REST fallback** (decided; the role fallback below is deliberately not copied). When the asset repository rejects the
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

## User role commands run the User role methods

`AddUserToRole` and `RemoveUserFromRole` (`Identity/Users/`) run the CK methods `System.Identity/User.AddRole-1` and
`User.RemoveRole-1` through `RtUserMethodClient`, which the source generator emits in **GraphQlDtos** from
`System.Identity-[3.2,4.0)` (`src/GraphQlDtos/construction-kits.yaml`, namespace `GraphQlDtos.DataTransferObjects.System.Identity.v3`;
pattern of the SDK migration guide, `octo-sdk/docs/method-client-migration-guide.md`, "Identity wave 1"). The REST methods
`IIdentityServicesClient.AddRoleToUser` / `RemoveRoleFromUser` are `[Obsolete]` in the octo-sdk; octo-cli calls them only as the
fallback below (`#pragma warning disable CS0618` at that one place). Command names, arguments, confirmation and log output are unchanged.

- **Flow** (`UserRoleMethodCall`): look the user up by name (`IIdentityServicesClient.GetUserByNameEmailOrId`, `UserDto.UserId` = runtime
  id), authenticate the tenant client (`ITenantClient` has its own access token; `PreValidate` only authenticates the identity client),
  call the method with the role **name** (the handler resolves it case-insensitively, like REST). `AddUserToRole` keeps its role
  existence check before the call; an unknown role is a `ServiceClientException` (exit -3; it used to only log and exit 0).
- **Older servers**: the role methods exist only with asset repository 3.5.43 and identity 3.5.52. A released
  server before that (e.g. a released installation on 3.5.23) rejects the document in GraphQL validation (`VARIABLES_ARE_INPUT_TYPES`,
  `KNOWN_TYPE_NAMES`, `FIELDS_ON_CORRECT_TYPE`, each with `extensions.number`). Then, and on `METHOD_HANDLER_NOT_AVAILABLE`, the call was
  certainly not executed: `UserRoleMethodCall` logs one information line (`The CK method … is not available (…); the role is changed
  through the REST endpoint of earlier identity versions`) and calls the REST method. The rule is `CkMethodFallback` (shared with `Dump`); any other refusal (`METHOD_FORBIDDEN`, declared errors) never falls back.
- **Refusals**: a declared error (`ROLE_NOT_FOUND`, `ROLE_ALREADY_ASSIGNED`, `ROLE_NOT_ASSIGNED`) becomes a `ServiceClientException` with
  the code and the message the REST endpoint answered before, e.g. `ROLE_ALREADY_ASSIGNED: User 'john.doe' already has role 'dataanalyst'`
  (Runner: error log, exit -3, as for a refused REST call). An undeclared refusal (`METHOD_FORBIDDEN` with `reason=self-grant` /
  `role-not-held` / `last-administrator`, `METHOD_TARGET_NOT_FOUND`) is the SDK's `CkMethodCallException`; `Runner` logs it with code and
  parameters (`CkMethodErrorText`), exit -3.
- `CreateUser` and `ResetPassword` run the password methods (previous section), without a REST fallback.
- Tests: `UserRoleMethodCommandTests` (generated client over a fake `ITenantClient`: method document and rtId, no REST role call,
  unchanged log lines, declared and undeclared refusals, declined confirmation, unknown user, unknown role fails; the validation
  answer of a 3.5.23 server and `METHOD_HANDLER_NOT_AVAILABLE` fall back to REST, a REST refusal is thrown, a refusal never falls back).

## Identity administration commands run the System.Identity methods

The write commands of groups, clients (create, update, `AddScopeToClient`, delete, roles, overlays, mirrors), API secrets of
clients and API resources, roles, API scopes, API resources, data permissions and policies, e-mail domain group rules, external
tenant user mappings, admin provisioning and identity providers (create, update, delete) run the CK methods of `System.Identity` through the method clients the source generator emits in **GraphQlDtos** from
`System.Identity-[3.8,4.0)` (`RtGroupMethodClient`, `RtClientMethodClient`, `RtRoleMethodClient`, `RtApiScopeMethodClient`,
`RtApiResourceMethodClient`, `RtDataPermissionMethodClient`, `RtDataPolicyMethodClient`, `RtEmailDomainGroupRuleMethodClient`,
`RtExternalTenantUserMappingMethodClient`, one client per concrete identity provider type; operation map: the octo-sdk
method client migration guide, "Identity wave 2"). Command names, arguments, confirmations and output are unchanged; the
reads (`Get*`, `LookupSourceTenantUser`) stay REST.

- **One helper** (`Identity/IdentityMethodCall`): authenticates the tenant client, runs the method, and maps the outcome. The method
  parameters are the ones the identity service's REST facade passes for the same request (its controllers are the reference), so a
  command does the same through either path.
- **Runtime ids**: the methods address their target by runtime id, the commands by name or client id. Clients are looked up with
  `GetClient` (`ClientDto.RtId`), roles with `GetRoleByName`, data permissions with `GetDataPermissions`, API scopes and resources with a
  GraphQL query of the tenant (`GraphQlConstants.GetApiScopeRtIdByName` / `GetApiResourceRtIdByName`; the REST DTOs carry no runtime id),
  identity providers with `GetIdentityProvider` (its concrete type selects the method client). Role ids of `CreateGroup`,
  `UpdateGroupRoles` and `UpdateClientRoles` are resolved to role names (`GetRoles`); an unknown id ends the command before any write with
  the REST message `Role with id '…' not found.`. The same holds for an unknown data permission (`Data permission '…' not found.`), an
  unknown API scope or resource, and an invalid data policy scope or enforcement mode (the REST endpoint's 400 text).
- **No REST fallback; roll out identity first.** These commands need octo-identity-services with the wave 2 methods
  (`System.Identity` 3.8.0, `IdentityMethodCall.RequiredSystemIdentityVersion`). When the asset repository rejects the method document
  in GraphQL validation, no handler is live (`METHOD_HANDLER_NOT_AVAILABLE`), or the identity service sends no runtime id, the command
  ends with `The service at '…' does not offer the CK method …. It needs identity services with System.Identity 3.8.0 or newer (the
  identity wave 2 methods)[ with the password methods switched on (Methods:Identity:EnablePasswordMethods)]. Nothing was changed.
  octo-cli does not fall back to the REST endpoint.` (exit -3). The bracketed part is printed for the secret-carrying methods
  (client create and update, the secret methods of clients and API resources, identity provider create and update), whose handlers
  the identity service offers on the method bus only with that switch. Roll out octo-identity-services with System.Identity 3.8.0
  (and the switch where these commands are used) before this octo-cli version. The user role commands keep their REST fallback.
- **Refusals**: a declared error (e.g. `GROUP_CYCLE`, `ROLE_ALREADY_EXISTS`, `MAPPING_ALREADY_EXISTS`) is a `ServiceClientException`
  `CODE: message (parameters)`; `METHOD_FORBIDDEN`, `METHOD_VALIDATION_FAILED` and `METHOD_TARGET_NOT_FOUND` reach the `Runner` as
  `CkMethodCallException` (code and parameters). Both exit with -3 and never fall back to REST. The declared errors the REST endpoint
  answered with success stay a success: `MEMBER_ALREADY_IN_GROUP`, `MEMBER_NOT_IN_GROUP`, `MEMBER_NOT_FOUND` (users; clients only for a
  runtime id), `CHILD_GROUP_ALREADY_IN_GROUP`, `CHILD_GROUP_NOT_IN_GROUP`, `ROLE_NOT_FOUND` of `RemoveClientFromRole`,
  `PERMISSION_ALREADY_GRANTED`, `PERMISSION_NOT_GRANTED`.
- **Secrets** (`docs/secrets-and-output.md`): `CreateApiSecretClient` / `CreateApiSecretApiResource` run `AddSecret`; the method
  generates the secret and returns its clear text once. The command prints it once, in the secret DTO the REST endpoint answered,
  with the stored value computed as the identity service stores it (Base64 of the SHA-256 of the UTF-8 clear text), and logs neither.
  The update and delete commands send the stored value only as a method parameter; every text of a refusal is scrubbed of it and
  thrown without the inner exception. The client secret of `AddClientCredentialsClient`, `AddDeviceCodeClient`,
  `AddAuthorizationCodeClient` and the identity provider commands travels only in the method's variables (read through `SecretInput`
  as before). Client create and update take the parameters the REST facade takes from the `ClientDto` (`Clients/ClientMethods`),
  identity provider create, update and delete call the client of the provider's concrete type (`IdentityProviders/IdentityProviderMethods`).
- **Output from the method result**: `ApplyClientOverlay`, `CleanClientOverlays`, `ProvisionClientInExistingTenants` and
  `ProvisionClientInTenant` log the counts of the result record; `CreateExternalTenantUserMapping` prints the mapping the REST endpoint
  answered, built from `RtAdminProvisioningResultRecordDto` as the identity service builds it; `ProvisionCurrentUser` names the identity
  of the result record; `CreateDataPolicy` logs the policy runtime id the method returns.
- Tests: `IdentityWave2MethodCommandTests` (every command: method document and runtime id, no REST write; `METHOD_FORBIDDEN` refused
  without a REST write and through the `Runner` with exit -3; declared errors with code, message and parameters; the success mappings;
  the version message without REST write for a server without the methods, without a handler and without a runtime id, the switch only
  for the secret-carrying methods; refusals echoing a secret value scrubbed; lookups that fail before any write; the outputs above; the
  GraphQL lookups validated against the asset repository schema). `CreateApiSecretCommandsTests` (no secret in the request, the clear
  text printed once with the computed stored value, never logged, also not on a refusal), `ApiSecretValueLoggingTests` and
  `CredentialsInRequestBodyTests` (the stored value only in the method's variables, never in a URL or log line),
  `SecretArgumentCommandTests` / `SecretCommandsTests` (client and identity provider secrets from prompt, stdin or environment reach
  the method), `CreateAdminProvisioningMappingConflictTests`, `CrossTenantGrantCommandTests`, `ProvisionCurrentUserOutputTests`
  (declared errors and results of the mapping methods), `ClientSecretCanaryTests` (real commands, SDK clients and `Runner` against a
  local identity and GraphQL listener, NLog at Trace: a client secret only in the GraphQL body, also on refusals that echo it; a
  generated secret exactly once on standard output and in no log line).

## GetClientRoles lists the direct roles of a client

`GetClientRoles -id <clientId>` (AB#9699) is read-only and prints the roles assigned **directly** to a client in the tenant of
the context, as a JSON array of `RoleDto` (`Id`, `Name`), sorted by name, `[]` when there are none. Roles the client gets
through a group are not listed. Standard output carries only that JSON; banner and log lines go to standard error
(`MachineReadableOutput.CommandNames`), so `octo-cli ... | jq` works. Operators use it to check a grant (e.g. `CommunicationManagement` for the CD deploy clients)
before and after a change; `--context` selects the tenant.

- **Flow**: two REST reads of the SDK, no CK method: `IIdentityServicesClient.GetRoles` (role id to name) and then
  `GetClientDirectRoles` (`GET {tenant}/v1/clients/{id}/roles`, role ids). No new server code.
- **Errors**: the tenant's roles are read first, so a `404` there means the tenant is unknown (the tenant middleware of the
  service answers it without a body) and the command stops with `Tenant '<t>' was not found at '<host>'.` before it reads the
  client. A `404` of the client's role endpoint ends with `Client '<id>' was not found in tenant '<t>'.`. Both are
  `ToolException`s, exit code `-5`, and print nothing on standard output. A `403` keeps the `Runner` path below (exit `-3`).
  On a real stack a context whose tenant never existed usually stops earlier, in `PreValidate`: the sign-in for that tenant
  falls back to the parent's token and its exchange for the tenant is refused ("Authentication failed: No token for tenant
  '<t>' was issued …", exit `-4`; verified on the local stack 2026-11-24). The `-5` tenant message covers a valid token reaching an identity service that does
  not know the tenant.
- **Role ids outside the tenant**: printed with `Name: null` and a warning, never dropped.
- **Access**: the identity directory read policy (`UserManagement`, `TenantManagement`, `AdminPanelManagement` or
  `CommunicationManagement`). Output and logs hold role ids and names and the client id only, never a secret.
- Tests: `GetClientRolesCommandTests` (real `IdentityServicesClient` against a local listener and the real `Runner`: success with
  names and order, no roles, role id outside the tenant, unknown client, unknown tenant without a client read).

## Client roles in a child tenant (`--child-tenant-id`)

`GetClientRoles`, `AddClientToRole` and `RemoveClientFromRole` take the optional `-ctid|--child-tenant-id <tenant>` (AB#9872),
the option name of their neighbours `ProvisionClientInTenant` and `UnprovisionClientFromTenant`. With it the command acts in
that child tenant of the context's tenant instead of the context's tenant, so an operator grants or checks a role of the
mirrored CD client in every customer tenant from one parent context (e.g. `octosystem`), without a context and a login per
tenant. Without the option nothing changes.

```bash
octo-cli --context <cluster>_octosystem -c GetClientRoles -id ci-deploy-<cluster> -ctid energyiq
octo-cli --context <cluster>_octosystem -c AddClientToRole -id ci-deploy-<cluster> -r CommunicationManagement -ctid energyiq
octo-cli --context <cluster>_octosystem -c RemoveClientFromRole -id ci-deploy-<cluster> -r CommunicationManagement -ctid energyiq -y
```

- **Flow** (`ChildTenantIdentityFactory`, `Commands/Implementations/Identity/Clients/ChildTenantIdentity.cs`):
  1. A tenant id equal to the context's tenant is refused (`Tenant '<t>' is the tenant of the context, not a child of it: omit
     --child-tenant-id.`).
  2. The descendants of the context's tenant are read with the context's token (`GetTenantDescendantsAsync`, the list of
     `GetTenants -r`). A tenant that is not among them - unknown, or below another parent - is refused before any token is
     requested: `Tenant '<t>' is not a child tenant of '<parent>' at '<host>': it does not exist or belongs to another parent.`
  3. The token for the child comes from `IChildTenantAccess`, the path of `Delete --disable-features` (unchanged): a
     signed-in user's token is exchanged for the child (RFC 8693; the identity service allows it for a descendant where the
     user is provisioned with a role, e.g. by `ProvisionCurrentUser -ttid <tenant>`), a client-credentials session (`OCTO_CLI_CLIENT_ID` and a secret set) requests a token for
     the client's mirror in the child (`OCTO_CLI_CHILD_CLIENT_SECRET` when the mirror has its own secret).
  4. The identity REST client and the GraphQL tenant client (CK methods `AddRole` / `RemoveRole`) are built for the child with
     that token. `IdentityMethodCall` gets a fixed-token authentication, so the context's token never reaches the child's
     routes. Options, output (`GetClientRoles`: the same JSON) and idempotency are the same as in the context's tenant;
     `RemoveClientFromRole` names the tenant in its confirmation question.
- **Rights**: the same as the other child-tenant commands - the caller may list the context's child tenants and holds a token
  for the child as a user provisioned there or as the client's mirror there (client credentials). The role the identity read or
  method needs is checked by the service in the child, as in the context's tenant.
- **Errors**, all without output on standard output and without a token in any message: not a child or the context's own
  tenant `-5`; no token for the child (exchange refused, no client-credentials variables) `-5` with the reason of
  `ChildTenantAccess`; a `403` of the descendants read or of the child (`Runner`, missing role or the `ProvisionCurrentUser`
  hint) `-3`; a refused method call (`METHOD_FORBIDDEN`, `ROLE_NOT_FOUND`) `-3` as without the option.
- **No server change**: the token is issued for the child, so the tenant authorization middleware sees a matching
  `tenant_id`. Verified on the local stack 2026-11-26 with a client-credentials session of `octosystem` and a scratch child:
  read, grant (twice, idempotent), read, remove; unknown tenant, the parent of the context and the context's own tenant
  exit `-5`; a grandchild where the client's mirror holds no role exits `-3` with the role message.
- Tests: `ClientRolesChildTenantCommandTests` (real `ChildTenantIdentityFactory` and `IdentityServicesClient` against a local
  listener that records the bearer token, real `Runner`: success with the child token on every request, upper-case id, a
  grandchild, unknown tenant, a tenant that exists but is no child, the context's own tenant, no token for the child, `403` in
  the child, `403` of the descendants read, no child call without the option; `AddClientToRole` / `RemoveClientFromRole` call
  the method on the child's clients only, the confirmation names the tenant, a refused child changes nothing,
  `METHOD_FORBIDDEN` exits `-3`).

## 403 names the missing role

`Runner` answers a `403` (`ServiceClientResultException`, exit code `-3`) with the `message` of the identity API body
(`{"message": "...requires the role 'SecretManagement'..."}`) printed verbatim; `ForbiddenResponseMessage` reads it once from
the exception text (the SDK keeps the body there). A 403 without body or without a usable `message` (the cross-tenant case) keeps the
generic text with the `ProvisionCurrentUser` hint. The notes of the ApiSecret write commands and `AddClientCredentialsClient` state that
writes need the tenant role `SecretManagement` (`TenantManagement`/`UserManagement` only while the cluster runs `SecretManagementRoleEnforcement=Warn`);
the command reference page is generated from them. Tests: `ForbiddenRoleMessageTests`.
