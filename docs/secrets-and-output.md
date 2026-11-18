---
description: Output redaction, passwords and secrets never on the command line, credentials only in request bodies.
applies_to: src/ManagementTool/Services/{ErrorOutputRedactor,RedactedLogOutput,SecretInput,SecretConsole,StandardStreams}.cs, src/ManagementTool/Commands/Implementations/Identity/**
---

# Secrets and credentials in input and output

## No credentials in output: ErrorOutputRedactor (AB#8570)

A failed server call surfaces as an SDK exception whose message is the **response body** (`ServiceClientResultException`:
`"<Status>: <body>"`). A service on the local stack runs with `UseDeveloperExceptionPage()`; its plain-text 500 page ends
with a `HEADERS` section that echoes the request headers, `Authorization: bearer …` and cookies included. That text used
to reach the console and the log file unchanged.

- **Central, not per command.** `Services/ErrorOutputRedactor.Redact` removes the developer page's header section
  (`[request headers removed by octo-cli]`), the values of credential headers (`Authorization`, `Proxy-Authorization`,
  `Cookie`, `Set-Cookie`, API key / auth token headers), bearer tokens, JWTs and credential-named JSON or form fields
  (`SecretMasker.IsSecretName`: `password`, `clientSecret`, `client_secret`, `access_token`, …) — `[REDACTED]`. Status,
  the server's message, method and URL stay.
- **Where it applies.** `Services/RedactedLogOutput.Enable()` (first line of `Program.Main`) wraps the layout of every
  NLog target (console, log file, counters) in a `RedactingLayout`, also for every later or reloaded configuration
  (`LogManager.ConfigurationChanged`), so a custom `nlog.config` at `Trace` level is redacted too. `StandardStreams.WriteError`
  (the GraphQL error body) redacts as well. Commands and `Runner` keep logging `ex.Message` as before; do not add
  per-command redaction, and do not print response bodies through `Console` directly.
- Tests: `ErrorOutputRedactionTests` (real `ResetPassword` + real `IdentityServicesClient` against a local listener that
  answers 500 with a developer page echoing the `Authorization` header and the request body - since AB#8418 to the user
  lookup that precedes the method call - through the real `Runner` and the `nlog.config` console layout; Info and Trace
  level; fake values only).

## Credentials in the request body: Delete/UpdateApiSecret* (AB#8008)

`DeleteApiSecretClient` / `DeleteApiSecretApiResource` / `UpdateApiSecretClient` / `UpdateApiSecretApiResource` go through the SDK
`IIdentityServicesClient`, which since AB#8008 sends the stored secret hash (`POST apiSecrets/{client|apiResource}/{id}/delete` and
`.../lookup`, body `{ valueEncrypted }`) only in the JSON body - never in the URL, where request logs, traces and proxy access logs
capture it. No command code changed; the behaviour comes with the SDK package. Tests: `CredentialsInRequestBodyTests` (real
command + real SDK client against a local listener: method, URL without the value, body). The test found that the SDK's
`UpdateApiSecret*` sent `POST` (the create endpoint) instead of `PUT`, so `UpdateApiSecretClient` / `UpdateApiSecretApiResource`
created an additional secret; fixed in the SDK with AB#8008.

`ResetPassword` used the same REST body form (`POST users/ResetPassword`, AB#8008) and recognized identity services before 3.5.55
(AB#8839); since AB#8418 it runs the CK method instead (next section), so that REST path and its older-identity detection are gone.

## Passwords and secrets never on the command line (AB#8044)

A value on the command line ends up in the shell history and in the process list (`ps`), where every user of the machine and
every process monitor can read it. The commands that take a password or secret therefore read it through
`Services/SecretInput` (`ISecretConsole` = console + environment, faked in tests by `FakeSecretConsole`). Sources, first match
wins:

1. the old argument (`-p`, `-s`, `-cs`) - **deprecated, still works in this release** (removal date to be announced, AB#8861),
   logs a warning naming the alternatives, never the value; it is no longer mandatory in the parser;
2. the stdin flag (`--password-stdin` / `-ps`, `--secret-stdin` / `-ss`, `--client-secret-stdin` / `-css`): standard input to its
   end, one trailing line break removed; empty input is an error; together with the old argument it is refused;
3. the environment variable;
4. a hidden prompt (no echo, prompt on stderr) - only for a **required** secret and only when standard input is a terminal; a
   secret that is set (new password, new client secret, identity-provider secret) is asked twice. Redirected input without a
   source fails and names the stdin flag and the variable.

| Command | Old argument | Stdin flag | Environment variable | Prompt |
|---|---|---|---|---|
| `ResetPassword` | `-p` | `--password-stdin` | `OCTO_CLI_NEW_PASSWORD` | yes, twice |
| `Setup` | `-p` | `--password-stdin` | `OCTO_CLI_NEW_PASSWORD` | yes, twice |
| `CreateUser` | `-p` | `--password-stdin` | `OCTO_CLI_NEW_PASSWORD` | no (password optional: without a source the user has none) |
| `AddClientCredentialsClient`, `AddDeviceCodeClient` | `-s` | `--secret-stdin` | `OCTO_CLI_NEW_CLIENT_SECRET` | yes, twice |
| `AddOAuthIdentityProvider`, `AddAzureEntryIdIdentityProvider` | `-cs` | `--client-secret-stdin` | `OCTO_CLI_IDP_CLIENT_SECRET` | yes, twice |
| `UpdateIdentityProvider` | `-cs` | `--client-secret-stdin` | `OCTO_CLI_IDP_CLIENT_SECRET` | no (without a source the stored secret is kept) |
| `LogInClientCredentials` | `-s` | `--secret-stdin` | `OCTO_CLI_CLIENT_SECRET` (as before) | yes, once |
| `RedeemAiTicket` | `-at`, `-rt` | - (two values) | `OCTO_CLI_AI_ACCESS_TOKEN`, `OCTO_CLI_AI_REFRESH_TOKEN` | no (all four values absent = dev fakes, as before) |

- Help-text samples and this file never show a password as an argument (AB#9010): `ResetPassword`, `CreateUser` and
  `Setup` show the prompt or `--password-stdin`; their notes name `-p/--password` as deprecated with the removal date to be
  announced; the interactive samples say they prompt only when `OCTO_CLI_NEW_PASSWORD` is not set. Checks, both find nothing:
  `git grep -nE 'octo-cli.*-c +(ResetPassword|CreateUser|Setup)( |$).*( -p( |$)|--password( |=|$))'` (rendered commands in
  docs and this file) and `git grep -nE 'CodeSampleArgument\(_[A-Za-z]*([Pp]assword|[Ss]ecret)Arg[,)]' -- src` (structured
  help samples, which become commands only in the generated reference).
- `ResetPassword` asks for confirmation **before** it reads the password; with `--password-stdin` standard input is a pipe, so
  `ConfirmationService` cannot ask and `-y` is needed.
- Not changed: `Update/DeleteApiSecret*` `-s` take the stored secret **hash** as an identifier (not the secret; logged masked,
  AB#6692); `Create*ApiSecret*` generate the secret. `ContextManager`/`contexts.json` tokens are not arguments.
- Generator constraint: `CommandReferenceGenerator` reads argument help only from collection literals (`["…", $"…{Constants.X}"]`)
  and resolves `Constants.*` only inside interpolations, so the help texts are written inline, not built by a helper.
- Tests: `SecretInputTests` (order, trimming, conflicts, prompt, redirected input), `SecretArgumentCommandTests` (every command
  above: stdin, environment, prompt, deprecated argument with warning and without the value in any log line).
