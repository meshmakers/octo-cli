---
description: Login flows, contexts.json, per-tenant token guard and exchange, the --context override, parallel invocations, token refresh and masking.
applies_to: **/Services/{Authentication,Context,LoginReplacement,SecretMasker}*.cs, **/Implementations/General/**, OctoCommandParser.cs
---

# Authentication, contexts and tokens

## Authentication Flow

The CLI supports multiple authentication methods:

1. **Device Code Flow** (recommended for interactive CLI use):
   - User runs `octo-cli -c LogIn -i`
   - CLI displays device code and verification URL
   - User authenticates in browser
   - CLI receives access token plus a refresh token (device flow requests `offline_access`)
   - Token stored in the effective context's `Authentication` block in `~/.octo-cli/contexts.json`
     (the active context, or the one named by `--context`)
   - **`--if-needed` (`-in`)**: non-disruptive variant. Before starting the device
     flow, the command checks whether the stored access token is still valid; if not,
     it tries a silent refresh-token exchange. The device flow is only started when
     both fail. Combine with `-i` so the browser opens automatically in that fallback
     case. Ideal for setup scripts that re-run often (e.g. tenant init) — the user is
     only prompted when the refresh token is missing or expired.

2. **Client Credentials Flow** (non-interactive — pipelines, cron jobs, headless servers, container entrypoints, batch scripts, bots, etc.):
   - Operator creates a per-tenant client once: `octo-cli -c AddClientCredentialsClient -id <id> -n "<name>"` (asks for the secret; `--secret-stdin` or `OCTO_CLI_NEW_CLIENT_SECRET` in scripts)
   - Caller exports `OCTO_CLI_CLIENT_ID` and `OCTO_CLI_CLIENT_SECRET` (or passes `-id` and pipes the secret in with `--secret-stdin`; `-s` is deprecated), then runs `octo-cli -c LogInClientCredentials`
   - Tenant comes from the effective context (no `-tid` arg on the login command). Switch contexts with `UseContext`, or target another tenant for one invocation with `--context <name>`.
   - No refresh token is issued. While the env vars remain set, `EnsureAuthenticated` automatically re-acquires the token when it expires; otherwise re-run `LogInClientCredentials`.
   - Token stored in the effective context's `Authentication` block, same path as device flow.
   - **Never silently over an interactive login.** Before it reads the secret or requests a token, the command
     refuses (`ToolException`, exit `-5`, nothing changed) and names the stored context when the context holds an
     interactive login (it has a refresh token, which a client-credentials login never has) or when the name given with
     `--context` / `OCTO_CLI_CONTEXT` matched the stored context only by case (`--context local_octosystem` selecting
     `local_OctoSystem`). `--replace-login` (`-rl`) replaces the login anyway; the usual alternative is a separate context
     for the client. Re-running the login on a client-credentials context with the exact name is unchanged. The rule is
     `Services/LoginReplacementGuard`; `IContextManager.RequestedContextName` keeps the name as typed next to the stored
     one. Reading commands keep matching names case-insensitively. The automatic fallback (refresh token
     rejected, client-credentials env vars set) still re-acquires a client-credentials token, because the interactive login
     is already dead there; it now logs a WARN that names the context and the `LogIn` to restore it. Tests:
     `LogInClientCredentialsContextGuardTests` (real `ContextManager` in a throwaway directory; `--context` and
     `OCTO_CLI_CONTEXT` paths).

## Token Storage & Context Management

The CLI uses named contexts (similar to `kubectl config use-context`) stored in `~/.octo-cli/contexts.json`. Each context holds service URIs, tenant ID, and authentication tokens independently. Context names are matched case-insensitively, so `UseContext -n Local_OctoSystem` resolves to a stored `local_octosystem` context:

```json
{
  "ActiveContext": "dev",
  "Contexts": {
    "dev": {
      "OctoToolOptions": {
        "IdentityServiceUrl": "https://localhost:5003/",
        "AssetServiceUrl": "https://localhost:5001/",
        "TenantId": "octosystem"
      },
      "Authentication": {
        "AccessToken": "eyJ...",
        "RefreshToken": "...",
        "AccessTokenExpiresAt": "2024-01-01T00:00:00Z"
      }
    },
    "prod": { "..." : "..." }
  }
}
```

**Migration**: On first run, if `contexts.json` does not exist but `settings.json` does, the CLI automatically imports the legacy settings as a `"default"` context. The original `settings.json` is kept intact.

Service URLs of a context: identity, asset, bot, communication, reporting, AI and, since the knowledge platform-update commands, `McpServiceUrl` (the MCP service; `AddContext -msu`, `Config -msu`, shown by `ListContexts`). Only those commands use it; a context without it works for every other command.

## Tokens per tenant — guard and exchange

A command addressing tenant X never sends a token whose `tenant_id` claim is another tenant. The tenant a
command addresses is the tenant of the effective context (`OctoToolOptions.TenantId`). Authority of a
root-tenant operator in a child tenant comes **only from explicit provisioning** there
(`octo-cli -c ProvisionCurrentUser -ttid <child>`, per child tenant, also for new tenants); identity gives
ancestor users no implicit admin role, and octo-cli has no allow-list or exemption.

- `AuthenticationService.ApplyAsync` is the only place that hands the context's token to the injected service clients
  (clients for a child tenant get their token from `ChildTenantAccess`; a few commands, e.g. `ImportFromCatalog` and
  `ReconfigureLogLevel`, copy the access token of a client that `ApplyAsync` already set). Token of the
  context's tenant: used as before (no exchange, no extra request). Token of another tenant (a context for a
  child tenant that was signed in on the root tenant): exchanged (RFC 8693, `AuthenticatorClient`, client
  `octo-cli`) for a token of the context's tenant, and **only the exchanged token** reaches the clients. Blank or
  opaque tokens and contexts without a tenant behave as before.
- The SDK does the same at its own choke point (`AssetServicesClient.CreateTenantScopeClientAsync`, see the
  octo-sdk CLAUDE.md), so a call that names another tenant explicitly cannot carry the wrong token either.
- **Cache:** exchanged tokens are kept per target tenant in `Authentication.TenantTokens` of the context entry
  (`ContextTenantTokenStore`), next to the token they were exchanged from and protected exactly like it (same
  file, nothing new is written elsewhere). Renewed silently 60 s before expiry or when the context's token
  changed; there is no refresh token and no re-login. Expired entries are dropped on write. An older octo-cli
  ignores the field.
- **Denial, no fallback:** the command stops (exit code -4) with a message that names the tenant and the cause:
  not provisioned (`ProvisionCurrentUser -ttid <tenant>`), access to the tenant denied (not a descendant, not
  provisioned), client not allowed to exchange (identity lacks the grant: use a context for the tenant), sign-in
  rejected (`octo-cli -c LogIn`), tenant unknown, identity unreachable. A plain HTTP 403 of the service becomes the
  same hint (provisioning), from the SDK for tenant scope calls and from `Runner` otherwise. The context's own
  token is never sent as a fallback. No message or log line contains token content.
- **Per-tenant contexts stay** for CI and machines (`LogInClientCredentials`, `deploy-workload.yml`): their token
  is issued for the context's tenant, so nothing is exchanged.
- Tests: `TenantTokenAuthenticationTests` (service, cache, denial causes), `TenantScopedCommandPathTests`
  (real commands LibraryStatus, ListBlueprintInstallations, ClearCache against a loopback listener: the request
  carries only the child token), shared fakes in `TenantTokenTestSupport`.

## Per-invocation context override (`--context`)

`UseContext` switches the active context persistently, which makes it useless for running two
commands against different environments at the same time. **`--context <name>` selects a stored
context for a single invocation and never touches `ActiveContext`**, so parallel invocations can
each target their own tenant/environment:

```bash
octo-cli --context prod_acme -c GetUsers      # runs against prod_acme
octo-cli -c GetAdapters --context prod_acme   # position does not matter
OCTO_CLI_CONTEXT=prod_acme octo-cli -c GetDeploymentSites
```

Precedence: `--context` > `OCTO_CLI_CONTEXT` > `ActiveContext`. An unknown name aborts with exit
code `-5` and lists the known contexts — there is no silent fallback to the active context.

The natural way to create the contexts is `Register-OctoCliContext -NoSwitch` in `octo-tools`
(`modules/Register-OctoCliContext.psm1`): it registers a context without switching to it, which is
exactly what `--context` then addresses.

Behaviour worth knowing:

- **It is a top-level argument, not a command argument.** It is declared by `OctoCommandParser`
  (`src/ManagementTool/OctoCommandParser.cs`) on the parser's top layer, so it works before or
  after `-c <command>` — also between the command's own options — and applies to *every* command. Accepted spellings are `--context`,
  `-context` and `/context`; **no short form** exists, deliberately, so the term cannot collide
  with an argument a future command declares. `CommandReferenceGenerator` only reads command
  classes, so this argument does not appear on any generated command-reference page — it is
  visible in `octo-cli -h all`.
- **Position independence is done by hoisting.** The parser's top layer cannot hand control
  back to a command layer: an argument the command does not know ends the command layer, so
  `-c FixAll --context x -w` used to fail with "-w is an unknown parameter". `ContextHoistingEnvironmentService`
  (registered as `IEnvironmentService` in `Program`) feeds the parser the line with every
  `--context <name>` pair moved in front by `ContextArgumentHoister`; nothing else is reordered.
  `ContextOptionOrderTests` runs the real parser with the context before, between and after flags
  and valued options. A new top-level argument needs the same treatment.
- **It resolves before the DI container is built.** `Program.BuildDi` fills `OctoToolOptions` and
  every service-client options object from the context, and the service clients are constructed
  while the command list is enumerated — all of that happens *before* the command line is parsed.
  `ContextSelection.Resolve` therefore pre-scans the raw arguments; the parser declaration exists
  for validation, help and a consistency check. When changing either side, keep
  `ContextOverrideResolver.IsContextTerm` in step with `Argument.Compare` in `mm-common`.
- **Tokens are written to the effective context.** `AuthenticationService.SaveAuthenticationData`
  uses `GetEffectiveContext()`, so `--context prod -c LogIn` logs into `prod` without activating
  it, and a refreshed token never lands in the wrong context.
- **`Config` refuses to run with `--context`** (exit `-5`): the override picks the context a
  command *acts on*, and rewriting a stored context definition through it is never the intent. Use
  `AddContext -n <name>` to write a named context. `UseContext` still switches the active context
  and logs a warning that `--context` does not apply to it.
- **`ListContexts` marks both**: `*` is the active context, `>` the one this invocation uses. The
  JSON output carries `isActive` and `isEffective`.

**`IContextManager` has two notions of "current"**: `GetActiveContext()` is the persisted
selection — use it only where that selection is itself the subject (`ListContexts`, `UseContext`).
Everything acting on "the context in use" wants `GetEffectiveContext()` / `SaveEffectiveContext()`.

## Parallel invocations and `contexts.json`

Because several processes can now write `contexts.json` concurrently (token refresh, `AddContext`),
every write in `ContextManager` goes through `Mutate`, which **takes a cross-process lock
(`contexts.lock`, `FileShare.None` with retries), re-reads the file, applies only its own change,
and replaces the file atomically** (temp file + `File.Move(overwrite: true)`). A reader therefore
never sees a half-written file, and two processes refreshing tokens for different contexts do not
overwrite each other. A plain "serialise my whole in-memory state" write — what the code did
before — loses one of them.

`Mutate` takes an `Action<ContextConfiguration>`, not a function returning one: a mutation can
adjust the configuration it is handed but cannot hand back a different instance, because returning
a wholesale replacement is precisely how a caller would discard a parallel invocation's changes
unnoticed. The single exception to the re-read is `MigrateIfNeeded`, which writes a whole
configuration. It checks that `contexts.json` is absent before it takes the lock, so two first-run processes can both
write, and the later write can replace a change the first one saved in the meantime.

For complete isolation (CI matrix jobs, throwaway environments), point each job at its own
configuration directory with **`OCTO_CLI_HOME`**: it names the *parent* of the `.octo-cli` folder,
so there is no shared file to contend for at all. Unset, the user profile is used as before. The
path in effect is logged on every run and shown by `ListContexts`.

**Note**: Device flow requests `offline_access` scope and receives a refresh token. The `AuthenticationService` handles both cases: if a refresh token is present, it refreshes expired access tokens automatically; if not, it uses the existing access token directly.

**Refresh decision & failure handling.** `EnsureAuthenticated` decides whether to refresh a device/interactive token from the stored `AccessTokenExpiresAt` (with a 30s margin), **not** from a live `/connect/userinfo` probe. A token that is not expiring soon is used as-is — no per-command round-trip to the identity service, and a transient identity-service hiccup no longer forces an unnecessary refresh. Server-side revocation of a not-yet-expired token still surfaces as the target service's own 401. When the refresh itself fails because the **refresh token is expired/revoked** — the typical outcome for a context that has not been used in a while, so it bites hardest when switching between contexts — the tool does one of two things: if `OCTO_CLI_CLIENT_ID` / `OCTO_CLI_CLIENT_SECRET` are set it re-acquires non-interactively via client_credentials; otherwise it fails with an actionable `ToolException` (exit `-5`) naming the effective context and the exact `octo-cli --context <name> -c LogIn` to run, instead of the raw OIDC `invalid_grant`. `AuthStatus` reports the same dead-refresh-token case as an actionable warning rather than letting it bubble up.

**`AuthStatus` masks secrets by default.** The output of `AuthStatus` lands in scripts, CI logs and AI-agent transcripts (it is the recommended silent-refresh command), so by default it prints only status, claims, expiry and the user-info response; the token signature and the raw access token are replaced by `SecretMasker.Mask` (`Services/SecretMasker.cs`, generic helper: reveals only the length). `--show-token` (`-st`) prints the raw access token, signature and refresh token; use it only interactively. Silent refresh behaves the same with and without the flag. Tests: `AuthStatusCommandTests`.

**`GetAdapter` masks credentials by default.** `GetAdapter` printed the adapter configuration and the pipeline configurations verbatim, including passwords (e.g. an EDA adapter). By default every credential-named member (`SecretMasker.IsSecretName`: password, passwd, passphrase, secret, apiKey, privateKey, accessKey, credential(s), connectionString, authorization, bearer, `*token`, `pwd`; status flags such as `ClientSecretIsSet` and names like `tokenEndpoint` stay visible) is replaced by `SecretMasker.Mask` (length only). The adapter configuration (a JSON string), the pipeline configurations (JSON) and the pipeline node definition (YAML/JSON, `key: value` / `key=value` lines) are walked by `SecretMasker.MaskToken` / `MaskText`; the output stays valid JSON. `--show-secrets` (`-ss`) prints the clear text; use it only interactively. Judged by member name only: the DTOs carry no CK secret flag. Other commands: `GetAdapters`, `GetDeploymentSites`, `GetWorkloadsByChart` and `GetAdapterPoolQueue` print status summaries without configuration and are unchanged. Tests: `GetAdapterSecretMaskingTests`.

**`Delete`/`Update` `ApiSecret*` never log the secret value.** `DeleteApiSecretClient`, `DeleteApiSecretApiResource`, `UpdateApiSecretClient` and `UpdateApiSecretApiResource` take the secret value (`-s`) as an argument to identify the secret; it is passed to the service but every log line (start, not-found, success) shows only `SecretMasker.Mask(secretValue)` (length only). The `Create*ApiSecret*` commands keep their intentional one-time clear-text console output and log neither clear text nor hash; `GetApiSecrets*` print the stored (hashed) value list as the identifier users need for `-s`. Tests: `ApiSecretValueLoggingTests`, `CreateApiSecretCommandsTests`.
