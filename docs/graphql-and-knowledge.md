---
description: The GraphQl command, SetKnowledgePageStatus, and validating hand-written GraphQL documents against the asset repository schema.
applies_to: src/ManagementTool/Commands/Implementations/Asset/{GraphQl,Knowledge}/**, src/ManagementTool/Services/MachineReadableOutput.cs, tests/ManagementTool.Tests/Fixtures/**
---

# GraphQL command and knowledge page status

## GraphQl: run a GraphQL query against any tenant (AB#7027)

`octo-cli -c GraphQl -tid <tenant> -q "<query>"` (`Asset/GraphQl/GraphQlCommand.cs`) sends one GraphQL query or mutation to
`tenants/<tenant>/GraphQL` and writes the response JSON, unmodified, to standard output. It replaces the scripts that read the
token out of `contexts.json` and call the route with curl or Python: the token is chosen by the SDK
(`IAssetServicesClient.SendGraphQlAsync`, see octo-sdk CLAUDE.md) with the same rule as every other tenant-addressed command
(AB#6836): a token for the addressed tenant is used as is, a token of another tenant (the usual case: a root-tenant context,
`-tid` a child) is exchanged for a token of that tenant, cached per tenant in the context entry, and the context token is
**never** sent as a fallback.

- **Arguments:** `-tid/--tenantId` (default: the tenant of the context; lower-cased), the document from `-q/--query <text>`,
  `-f/--file <path>` or standard input (`-si/--stdin`, and also when nothing else is given and input is piped; only one source),
  `-vars/--variables <json object | @file>`, `-on/--operationName`. Works with `--context <name>` like every command.
- **Output for scripts:** standard output carries the answer and nothing else. The banner, context lines and all log output
  go to standard error for this command: `MachineReadableOutput` (pre-scan of the raw command line in `Program.Main`, like the
  context selection) switches the console log target to `StdErr`; its list `CommandNames` is the place to register another
  command with this property. The body is written as the service sent it plus a final line break if it had none, so
  `octo-cli ... | jq` and `ConvertFrom-Json` work. `GetOperation` and `ListOperations` (AB#8446) are registered too, so their
  `-j` output can be piped. The other commands print JSON through `IConsoleService`/the logger on standard output with banner
  and log lines mixed in.
- **Exit codes:** 0 success. GraphQL errors (non-empty `errors`, HTTP 200 or 400) print the body to standard error, nothing to
  standard output, exit `-5`. A tenant where the operator is not provisioned, an exchange denial or a 403: the AB#6836 message
  (tenant, cause, step `ProvisionCurrentUser -ttid <tenant>`), exit `-4`, no request is sent for a denied exchange.
  Missing/ambiguous document or invalid variables: exit `-5`. Transport/5xx: `-3`.
- **No token exposure:** the command logs the service URI and the tenant only; no token, header or request body is logged at any
  level, and exception texts do not contain token content (canary-token tests).
- **Tests:** `GraphQlCommandTests` (arguments, sources, stdout/stderr, exit behaviour, fake client), `GraphQlCommandPathTests`
  (real command, `AuthenticationService`, `AssetServicesClient`; loopback listener proves the tenant token, not the context
  token, reaches `/tenants/<t>/GraphQL`; canary context token appears in no output, log or exception; denials send nothing),
  `MachineReadableOutputTests`.
- The command is added to the `octo` skill command reference by AB#6840, not here.

## SetKnowledgePageStatus: the human path to a knowledge page status (AB#8565)

`octo-cli -c SetKnowledgePageStatus [-tid <tenant>] -s <slug> -st <Draft|InReview|Verified|Rejected> [-n <note>]`
(`Asset/Knowledge/SetKnowledgePageStatusCommand.cs`). Decision G2a (Epic AB#8449): **Verified is never set over MCP**; MCP
callers are always AI clients, so octo-mcp-service `knowledge_set_status` refuses `Verified` for every caller
(`VerifiedNotAllowedOverMcp`). A person sets it with this command (later: the Refinery Studio review view).

- **User login only**: the context's access token must carry a `sub` claim (interactive `LogIn`). A client-credentials
  context (no `sub`) or no token is refused **before** anything is sent. The server check stays the caller's CK roles.
- **Author = the subject the tenant sees**: for a context of another tenant (root-tenant login, `-tid` a child) the author is
  the `sub` of the exchanged token (`ITenantTokenProvider`, the same cached exchange the service client uses), i.e. the
  user's id in the target tenant, as the knowledge tools record it — not the root-tenant id.
- **Same write as the knowledge tools**: reads the page over GraphQL (`systemAiKnowledgePage`, filter on `rtWellKnownName`),
  then one mutation request with two root fields (asset repo: one request = one transaction, root mutation fields run in
  order): page update (`verificationStatus`, `contentHash`, `currentVersion`, `compiledAt`) and a new
  `KnowledgePageVersion` `<slug>@t<n>` (the tenant key namespace, AB#9103: `<slug>@<n>` belongs to the blueprint seed; next free number, below) with the full content, author `user:<sub>`, `authoredAt`, `changeNote` (the note, or
  `status <Status>`) and the `versionOf` association. A plain GraphQL update of `verificationStatus` is not the documented
  path (it skips the version).
- **Version number and chain (AB#8968)**: before the write a second query reads the page's versions (`MATCH_REG_EX`
  `^<slug>@t?[0-9]+$`, sorted by `versionNumber` descending) and the input fields of `SystemAiKnowledgePageVersionInput`
  (`__type`). The new version is `max(CurrentVersion, highest existing version) + 1`, so a seeded Blueprint version not
  adopted yet is never reused (octo-mcp-service `KnowledgePages.BuildWrite` + `HighestVersionAsync`). On System.Ai 4.11.0
  or newer it records `baseVersion` = the replaced `CurrentVersion`, so the ancestor chain of the platform-update rule
  (AB#8780, `KnowledgePlatformUpdates.Ancestors`) stays intact; older tenants get no `baseVersion` (the field does not
  exist there). **Rule (Odin, AB#8968):** a page a person verified counts as edited; a newer Blueprint version is reported
  as `PlatformUpdateAvailable` (adopt = `knowledge_restore_version`, keep = `knowledge_dismiss_platform_update`) and never
  switched automatically, while the Blueprint versions in its chain are not reported again. The schema test validates the
  `baseVersion` write against the ai-sandbox snapshot plus a one-field overlay (`ValidateAgainstSystemAi411Async`) until
  the snapshot is refreshed from a 4.11.0 tenant.
- **Content hash contract**: `KnowledgePageContent.Render` is a port of octo-mcp-service `KnowledgePages.Render` (markdown
  with version 0; hash = SHA-256 lower-case hex). Both repos pin the same golden hash
  (`SetKnowledgePageStatusCommandTests.ContentHash_MatchesTheGoldenHashOfTheMcpService` here,
  `KnowledgeContentHashGoldenTests` there): a change of the render on one side must change the other in the same step.
- **Concurrent writes**: GraphQL has no conditional update, and the read is not in the mutation's transaction. A
  knowledge-tool write that commits between the read and the mutation would leave the page with new content but the old
  `contentHash`, a status set on content the person did not review, and two versions with the same number. The command
  therefore re-reads the page after the write and fails with a conflict message ("changed at the same time … read the page
  again and set the status again") when version, status or content hash differ from what it wrote. It detects, it cannot
  prevent; a conditional update in the asset repository would.
- **Limits**: the note (max. 500 characters) is not checked by the MCP content guard. Duplicate slugs are refused.
- Tests: `SetKnowledgePageStatusCommandTests` (golden hash, mutation variables, author from the exchanged token,
  client-credentials refusal, concurrent-write conflict, errors, and every sent request validated against the asset
  repository schema, see below).

## GraphQL documents against the asset repository schema (AB#8588)

Tests that fake `SendGraphQlAsync` accept any document; the asset repository validates it and answers HTTP 200 with
`errors`. `SetKnowledgePageStatus` shipped with `query knowledgePage($slug: String!)` for `fieldFilter.comparisonValue`
(type `SimpleScalar`) and failed on every real server ("Variable '$slug' of type 'String!' used in position expecting
type 'SimpleScalar'"). Hand-written GraphQL documents are therefore validated in the tests:

- `tests/ManagementTool.Tests/AssetRepositoryGraphQlSchema.cs` builds the schema from an introspection snapshot of a real
  asset repository (`Fixtures/asset-repo-schema.test-2-ai-sandbox.json.gz`: test-2, tenant `ai-sandbox`, System models
  only, descriptions stripped; `ValidateAgainstSystemBotAsync` uses `Fixtures/asset-repo-schema.test-2-system-bot-3.7.json.gz`,
  a test-2 scratch tenant with System.Bot 3.7.0 and `runtime.operations`, AB#8446) with **GraphQL.NET 8.8.5, the library and version of the asset repository**, and runs its
  core validation rules on a document **with its variables** (field and argument names, variable types in their
  positions, input object fields and enum values). The repository's own scalars (`SimpleScalar`, `OctoObjectId`,
  `DateTime`, …) accept any value: their value formats are not checked.
- Use it for every hand-written GraphQL document: capture what the command sends from the fake and call
  `AssetRepositoryGraphQlSchema.ValidateAsync(document, variablesJson)`; expect no errors
  (`SetKnowledgePageStatusCommandTests.EverySentRequest_IsValidAgainstTheAssetRepositorySchema`).
- **Refresh the snapshot** when a document needs a type it does not have (a newer System model, another CK model).
  Read-only, with any user context of a tenant that has the models:
  `octo-cli -c GraphQl --context <ctx> -tid <tenant> -f tests/ManagementTool.Tests/Fixtures/asset-repo-introspection.graphql > schema.json`,
  then `jq -c 'walk(if type=="object" then del(.description) else . end)' schema.json | gzip -9 -n > tests/ManagementTool.Tests/Fixtures/asset-repo-schema.<env>-<tenant>.json.gz`
  and adjust the file name in `AssetRepositoryGraphQlSchema`. Only System models belong in it: the repository is public.
- Limit: the snapshot is a point in time; a later breaking schema change on the server is not seen until the snapshot is
  refreshed.
