# Changelog

All notable changes to octo-cli are listed here. Newest first.

## Unreleased

### Fixed

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
