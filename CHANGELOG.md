# Changelog

All notable changes to octo-cli are listed here. Newest first.

## Unreleased

### Added

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
  the password with "Forgot password" on the sign-in page. octo-cli still never sends the password in the URL.
- **AB#8570 — no credentials in error output.** A failed server call no longer prints the request's
  `Authorization` header, cookies, bearer tokens or credential fields of a request body. A service with the
  ASP.NET Core developer exception page (the local stack) answers a 500 with a page that echoes the request
  headers; that body reached the console and the log file as part of the error message. Every log line (console,
  log file, every level, exception texts included) and the GraphQL error body on standard error now pass through
  one central redaction (`ErrorOutputRedactor`, wired into the NLog layouts by `RedactedLogOutput`). Status and
  the server's error message stay visible; the developer page's header section is replaced by
  `[request headers removed by octo-cli]`, credential values by `[REDACTED]`. A custom `nlog.config` with a
  lower log level is redacted the same way.
