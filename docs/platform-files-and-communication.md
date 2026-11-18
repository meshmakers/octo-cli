---
description: Platform file system commands, adapter pool queue, adapters, pipelines, triggers, pools and data flows.
applies_to: src/ManagementTool/Commands/Implementations/{Communication,Ai}/**, src/ManagementTool/Commands/Implementations/Asset/PlatformFiles/**
---

# Platform files and communication commands

## Platform file system commands (AB#6182)

`GetFiles`, `UploadFile` and `DownloadFile` (`Commands/Implementations/Asset/PlatformFiles/`) wrap the typed
`IAssetServicesClient.Files` client of the SDK (System.Files, AB#6171: bytes over REST `/{tenant}/v1/files`,
metadata over GraphQL). Files are addressed by root well-known name (default `Files`) plus `/`-separated path,
`DownloadFile` also by `-id <rtId>`. `UploadFile -c fail|replace|keepBoth` maps to `FileConflictMode`;
`DownloadFile` streams to a `.part` file and moves it into place, and refuses to overwrite an existing local file
without `-o`. No tenant feature must be enabled; the MCP counterparts are `list_files`, `upload_file`,
`download_file` (octo-mcp-service). Their reference pages are generated from the command classes like all others.

## Adapter pool queue (AB#4924 §10)

`GetAdapterPoolQueue -id <adapterPoolRtId>` and `CancelQueuedExecution -id <adapterPoolRtId> -eid
<executionId>` are the CLI third of the queue surface concept §5 asks for — the same view Refinery
Studio and the MCP server show, all three off one endpoint
(`GET`/`DELETE {tenantId}/v1/adapterPool/{id}/queue[/{executionId}]`). Three things about them are
deliberate and must survive future edits:

- **There is no global queue position and printing one would be wrong.** The pool serves borrowing
  tenants round-robin, so the truthful answer is the pair *position inside its own tenant* +
  *tenants ahead in the rotation*: item 1 of the tenant whose turn is next runs before item 2 of the
  tenant being served now. `AdapterPoolQueueCommandTests` pins that the pair is printed and that the
  word "rank" appears nowhere.
- **`CancelQueuedExecution` cancels a QUEUE entry, never a running pipeline.** An execution that
  already holds a lease comes back as `AdapterPoolQueueCancellationOutcome.AlreadyLeased` (HTTP 409)
  and is reported as its own case — *nothing was cancelled, interrupting it is a different
  operation* — not as a generic failure and never as a success. Collapsing the two verbs would leave
  the operator unsure which one they just performed.
- **An empty queue is an idle pool, not an error**, and the route tenant is the **lending** tenant:
  the pool is its entity, while every entry belongs to a borrowing tenant.

Manual (non-pooled) adapters have no queue at all and therefore no equivalent command — that
asymmetry is intended.

🔴 **`CommandReferenceGenerator.Tests` cannot run from a scratch `-p:BaseOutputPath`.**
`RoslynExtractorTests.InheritedArgsByBaseClass_does_not_drift_from_JobWithWaitOctoCommand_source`
walks up from `AppContext.BaseDirectory` looking for `Octo.Cli.sln` and asserts it found it, so an
assembly built outside the repo fails with `Assert.NotNull() Failure: Value is null` — a red test
that says nothing about the code. Build *that* project into the repo's own `bin/` (or run
`dotnet test`) and keep the scratch output path for `ManagementTool.Tests`, which has no such
dependency.

## Communication Services Commands

All communication commands accept plain runtime object IDs (e.g. `69cfa838092b710403248acd`). The SDK client internally constructs composite RtEntityId strings where the server requires them.

### Adapters

| Command | Parameters | Description |
|---------|-----------|-------------|
| `GetAdapters` | `--json` (optional) | List all adapters for the tenant |
| `GetAdapter` | `--identifier <rtId>`, `--json`, `--show-secrets` (optional) | Get adapter configuration; credentials masked unless `--show-secrets` (AB#6286) |
| `GetAdapterNodes` | | List available pipeline nodes from connected adapters |
| `GetPipelineSchema` | `--adapterId <rtId>`, `--outputFile` (optional) | Get pipeline JSON schema for an adapter |
| `RotateAdapterServiceAccountSecret` | `--identifier <rtId>`, `--yes` (optional) | Rotate the secret of the adapter's pipeline service account (destructive — prompts) |
| `SwitchServiceAccountsToImpersonation` | `--apply` (optional, default dry run), `--redeployDataFlows <true\|false>` (default true), `--yes`, `--json` | Dry-run or apply the switch of the tenant's pipeline service accounts to impersonation (apply is destructive — prompts) |

#### `RotateAdapterServiceAccountSecret` (AB#5032 / AB#5048)

Backs `POST {tenantId}/v1/adapter/{adapterRtId}/serviceAccount/rotateSecret` via
`ICommunicationServicesClient.RotateServiceAccountSecretAsync`. Three properties of it are
deliberate and easy to undo by accident:

- **It is the only supported rotation path.** The secret attribute is runtime state, so a blueprint
  can no longer change a live secret — an attempt fails confusingly. The controller owns both halves
  of the credential (identity client + the tenant's `ServiceAccountConfiguration`), which is why
  neither the CLI nor Studio may reproduce the rotation themselves.
- **The redeploy hint is printed, not swallowed** — as a `Warning`, and the controller's own message
  is relayed verbatim so CLI output and the tenant's audit event cannot drift apart. The adapter
  freezes the credentials into the pipeline's `GlobalConfiguration` at *pipeline registration* and
  never refreshes them, so until `DeployDataFlow` / `DeployPipeline` runs, every pipeline still
  presents the withdrawn secret. Skipping it is what produces "rotation done, still broken".
  When the adapter had no service account at all (`WasCreated`), nothing was invalidated and no
  warning is emitted — a redeploy demand nobody needs is how the real one gets ignored later.
- **No secret is printed**, because the response carries none. Both the controller and
  `Communication.Contracts.Tests` pin that.

#### `SwitchServiceAccountsToImpersonation` (AB#5537 / AB#6642)

Backs `POST {tenantId}/v1/serviceAccount/switchToImpersonation` via
`ICommunicationServicesClient.SwitchServiceAccountsToImpersonationAsync`. Dry run is the default and
changes nothing; `-a/--apply` runs it after a confirmation (`-y` skips it). Prints one line per
account and a count per outcome (`-j` prints the server report as JSON). Exit codes: server 409
(`OCTO_SERVICEACCOUNTIMPERSONATION__ALLOWSECRETCLEARING` not set) and any `Failed` account or failed
data-flow redeploy give `-5` (the full report is printed first); `SwitchedPendingInvalidation` only
warns that a re-run with `--redeployDataFlows true` completes the switch. The report carries no
secret or token and the command prints only report fields (pinned by tests).

### Pipelines

| Command | Parameters | Description |
|---------|-----------|-------------|
| `GetPipelineStatus` | `--identifier <rtId>`, `--json` (optional) | Get deployment state of a pipeline |
| `DeployPipeline` | `--adapterId <rtId>`, `--pipelineId <rtId>`, `--file <path>` | Deploy pipeline definition (YAML/JSON file) |
| `ExecutePipeline` | `--identifier <rtId>`, `--inputFile <path>` (optional) | Execute a pipeline, returns execution ID |
| `GetPipelineExecutions` | `--identifier <rtId>`, `--json` (optional) | List pipeline execution history |
| `GetLatestPipelineExecution` | `--identifier <rtId>`, `--json` (optional) | Get the most recent pipeline execution; `FailureKind` (AB#8156) is shown as `OutcomeUnknown` / `Failed` / `null` |
| `GetPipelineDebugPoints` | `--identifier <rtId>`, `--executionId <guid>`, `--json` (optional) | Get debug points for a specific execution |
| `SetPipelineDebug` | `--identifier <rtId>`, `--enabled <true\|false>` | Enable or disable debug capture for a pipeline |
| `GetPipelineDebug` | `--identifier <rtId>`, `--json` (optional) | Get the debug state of a pipeline |

`GetPipelineExecutions` and `GetLatestPipelineExecution` print the execution JSON through `PipelineExecutionOutput` (AB#8156):
`FailureKind` (octo-sdk `PipelineExecutionDataDto`, AB#8037 kind of the controller) appears as its name - `"OutcomeUnknown"` when a
write was not acknowledged and may have taken effect, `"Failed"` for an ordinary failure, `null` otherwise (no failure, an adapter
that reported no kind, interrupted or offline-buffered executions) - instead of the enum number. Other members keep their
rendering (`Status` stays the number). Tests: `PipelineExecutionFailureKindOutputTests`.

### Triggers

| Command | Parameters | Description |
|---------|-----------|-------------|
| `DeployTriggers` | | Deploy all pipeline triggers for the tenant |
| `UndeployTriggers` | | Undeploy all pipeline triggers for the tenant |

### Pools

| Command | Parameters | Description |
|---------|-----------|-------------|
| `GetDeploymentSites` | `--json` (optional) | List all deployment sites for the tenant |
| `DeployDeploymentSite` | `-id <deploymentSiteRtId>` | Trigger deployment-site deploy (operator creates the DeploymentSite CR); workloads deploy separately |

### Data Flows

| Command | Parameters | Description |
|---------|-----------|-------------|
| `DeployDataFlow` | `--identifier <rtId>` | Deploy a data flow |
| `UndeployDataFlow` | `--identifier <rtId>` | Undeploy a data flow |
| `GetDataFlowStatus` | `--identifier <rtId>`, `--json` (optional) | Get aggregated execution status of a data flow |

### Examples

```bash
# List all adapters
octo-cli -c GetAdapters

# Get adapter config as compact JSON
octo-cli -c GetAdapter --identifier 69cfa838092b710403248acd --json   # credentials masked; add --show-secrets for clear text

# Deploy a pipeline from YAML file
octo-cli -c DeployPipeline --adapterId 69cfa838092b710403248acd --pipelineId cc0000000000000000000003 --file pipeline.yaml

# Execute a pipeline and capture the execution ID
octo-cli -c ExecutePipeline --identifier cc0000000000000000000003

# Check data flow status
octo-cli -c GetDataFlowStatus --identifier cc0000000000000000000002

# Enable debug capture for a pipeline
octo-cli -c SetPipelineDebug --identifier cc0000000000000000000003 --enabled true

# Disable debug capture for a pipeline
octo-cli -c SetPipelineDebug --identifier cc0000000000000000000003 --enabled false

# Get pipeline debug state as formatted JSON
octo-cli -c GetPipelineDebug --identifier cc0000000000000000000003

# Get pipeline debug state as compact JSON
octo-cli -c GetPipelineDebug --identifier cc0000000000000000000003 --json
```
