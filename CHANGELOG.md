# Changelog

All notable changes to octo-cli are listed here. Newest first.

## Unreleased

### Fixed

- **AB#8570 — no credentials in error output.** A failed server call no longer prints the request's
  `Authorization` header, cookies, bearer tokens or credential fields of a request body. A service with the
  ASP.NET Core developer exception page (the local stack) answers a 500 with a page that echoes the request
  headers; that body reached the console and the log file as part of the error message. Every log line (console,
  log file, every level, exception texts included) and the GraphQL error body on standard error now pass through
  one central redaction (`ErrorOutputRedactor`, wired into the NLog layouts by `RedactedLogOutput`). Status and
  the server's error message stay visible; the developer page's header section is replaced by
  `[request headers removed by octo-cli]`, credential values by `[REDACTED]`. A custom `nlog.config` with a
  lower log level is redacted the same way.
