---
description: Platform file system commands, adapter pool queue, adapters, pipelines, triggers, pools and data flows.
applies_to: **/Implementations/{Communication,Ai}/**, **/Asset/PlatformFiles/**
---

# Platform files and communication commands

## Platform file system commands

`GetFiles`, `UploadFile` and `DownloadFile` (`Commands/Implementations/Asset/PlatformFiles/`) wrap the typed
`IAssetServicesClient.Files` client of the SDK (System.Files: bytes over REST `/{tenant}/v1/files`,
metadata over GraphQL). Files are addressed by root well-known name (default `Files`) plus `/`-separated path,
`DownloadFile` also by `-id <rtId>`. `UploadFile -c fail|replace|keepBoth` maps to `FileConflictMode`;
`DownloadFile` streams to a `.part` file and moves it into place, and refuses to overwrite an existing local file
without `-o`. No tenant feature must be enabled; the MCP counterparts are `list_files`, `upload_file`,
`download_file` (octo-mcp-service). Their reference pages are generated from the command classes like all others.

## Adapter pool queue

`GetAdapterPoolQueue -id <adapterPoolRtId>` and `CancelQueuedExecution -id <adapterPoolRtId> -eid
<executionId>` are the CLI third of the queue surface concept §5 asks for — the same view Refinery
Studio and the MCP server show, all three read one endpoint
(`GET {tenantId}/v1/adapterPool/{id}/queue`; the cancel runs the method `AdapterPool.CancelQueuedExecution-1`). Three things about them are
deliberate and must survive future edits:

- **There is no global queue position and printing one would be wrong.** The pool serves borrowing
  tenants round-robin, so the truthful answer is the pair *position inside its own tenant* +
  *tenants ahead in the rotation*: item 1 of the tenant whose turn is next runs before item 2 of the
  tenant being served now. `AdapterPoolQueueCommandTests` pins that the pair is printed and that the
  word "rank" appears nowhere.
- **`CancelQueuedExecution` cancels a QUEUE entry, never a running pipeline.** An execution that
  already holds a lease comes back as the declared method error `EXECUTION_ALREADY_LEASED` (REST: 409)
  and is reported as its own case — *nothing was cancelled, interrupting it is a different
  operation* — not as a generic failure and never as a success. Collapsing the two verbs would leave
  the operator unsure which one they just performed.
- **An empty queue is an idle pool, not an error**, and the route tenant is the **lending** tenant:
  the pool is its entity, while every entry belongs to a borrowing tenant.

Manual (non-pooled) adapters have no queue at all and therefore no equivalent command — that
asymmetry is intended.

## Communication Services Commands

All communication commands accept plain runtime object IDs (e.g. `69cfa838092b710403248acd`). The SDK client internally constructs composite RtEntityId strings where the server requires them.

### Write commands run the System.Communication methods

The write commands below call the CK methods of `System.Communication` through the method clients generated in
`GraphQlDtos` (`construction-kits.yaml`: `System.Communication-[4.19,5.0)`, together with `System-[2.7,3.0)`, whose
`Configuration` DTOs the communication types derive from). The call goes to the asset repository of the context's tenant
(GraphQL mutation `runtime.systemCommunication<Type>s.<method>`), and the communication controller runs the handler. Command
names, arguments, confirmations and output are unchanged; the reads (`Get*`), `EnableCommunication`,
`DisableCommunication`, `Get/SetCommunicationLifecycle`, `UpdateWorkloadResources` and
`SwitchServiceAccountsToImpersonation` stay REST.

| Command | CK method (`System.Communication/…`) | Notes |
|---|---|---|
| `DeployPipeline` | `Pipeline.Deploy-1` | target = the pipeline; the definition is a sensitive parameter |
| `ExecutePipeline` | `Pipeline.Execute-1` | prints the execution id as before |
| `SetPipelineDebug` | `Pipeline.SetDebugging-1` | |
| `MovePipelinesToAdapter` | `Pipeline.MoveToAdapter-1`, once per pipeline | each pipeline is reported on its own; a declared or platform error of one pipeline (unknown pipeline, `METHOD_TIMEOUT`, ...) is a failed item and the rest still move; only `METHOD_FORBIDDEN` (the caller) ends the command |
| `DeployDataFlow` / `UndeployDataFlow` | `DataFlow.Deploy-1` / `Undeploy-1` | |
| `DeployDeploymentSite` / `UndeployDeploymentSite` | `DeploymentSite.Deploy-1` / `Undeploy-1` | |
| `DeployTriggers` / `UndeployTriggers` | `PipelineTrigger.DeployAll-1` / `UndeployAll-1` | static: every trigger of the tenant |
| `DeployWorkload` / `UndeployWorkload` | `DeployableWorkload.Deploy-1` / `Undeploy-1` | on the client of the workload's concrete type |
| `UpdateWorkloadChartVersion` | `DeployableWorkload.SetChartVersion-1` | on the client of the workload's concrete type |
| `CancelQueuedExecution` | `AdapterPool.CancelQueuedExecution-1` | `EXECUTION_ALREADY_LEASED` / `QUEUED_EXECUTION_NOT_FOUND` are reported as before (warning, exit 0) |
| `RotateAdapterServiceAccountSecret` | `Adapter.RotateServiceAccountSecret-1` | the result carries no secret |

`CommunicationMethodCall` (in `Commands/Implementations/Communication/`) runs every call:

- **Role.** Every method requires the role `CommunicationManagement` (and the scope `octo_api`). The REST endpoints let any
  `octo_api` caller through, so a user or client credentials client without the role that used these commands before now
  gets `METHOD_FORBIDDEN` (exit -3). Grant the role before switching a client (for example a CD client) to this octo-cli
  version.
- **Workload type.** The workload methods are declared on the abstract `DeployableWorkload` and run through the client of
  the concrete type (`Adapter`, `AdapterPool` or `Application`). The command reads the workload's `ckTypeId` first
  (`GraphQlConstants.GetDeployableWorkloadCkTypeById`) and compares its version-less form: the server answers
  element-versioned ids such as `System.Communication/Adapter-1`, and a newer model may answer `-2`. An unknown workload
  ends the command before any write (`Workload '<id>' not found.`).
- **Runtime ids.** An argument that is no runtime id ends the command before any call (`<Name> '<value>' is not a valid
  id.`); otherwise the asset repository would refuse the variable in GraphQL validation, which reads like a service
  without the method.
- **No REST fallback.** A service without the method (the asset repository rejects the document in GraphQL validation, or
  `METHOD_HANDLER_NOT_AVAILABLE`) ends the command with a message that names communication controller services with
  System.Communication 4.19.0. Nothing was changed in that case. Roll out the communication controller with these methods
  before this octo-cli version. With `METHOD_HANDLER_NOT_AVAILABLE` the message also names a controller restart: the
  handler is missing for a moment while the communication controller restarts.
- **Errors.** A declared error of the method prints `CODE: message (parameters)` and exits with -3, as the refused REST call
  did. Platform errors (`METHOD_FORBIDDEN`, `METHOD_VALIDATION_FAILED`, `METHOD_TARGET_NOT_FOUND`, `METHOD_TIMEOUT`) print
  their code and parameters and exit with -3. A success without the result record of `ExecutePipeline`,
  `SetPipelineDebug` or `RotateAdapterServiceAccountSecret` ends with -3 too (`returned no result`).
- **Deployment pipelines.** The workload rollout pipelines run `GetWorkloadsByChart` (REST read, unchanged),
  `UpdateWorkloadChartVersion` and `DeployWorkload`; they judge the two writes by their exit code and only log their
  output. Both commands log the same lines as before; the warning a deploy may return (an explicit Helm value overrode a
  typed sizing setting) is logged at debug level only and stays readable on the workload (`lastDeploymentWarning`).

Tests: `CommunicationWave3MethodCommandTests` (every command: method path, no REST write, refusal, version message,
workload type with element versions `-1`/`-2`, the log lines of the two CD commands), `AdapterPoolQueueCommandTests`,
`RotateAdapterServiceAccountSecretCommandTests`.

### Adapters

| Command | Parameters | Description |
|---------|-----------|-------------|
| `GetAdapters` | `--json` (optional) | List all adapters for the tenant |
| `GetAdapter` | `--identifier <rtId>`, `--json`, `--show-secrets` (optional) | Get adapter configuration; credentials masked unless `--show-secrets` |
| `GetAdapterNodes` | | List available pipeline nodes from connected adapters |
| `GetPipelineSchema` | `--adapterId <rtId>`, `--outputFile` (optional) | Get pipeline JSON schema for an adapter |
| `RotateAdapterServiceAccountSecret` | `--identifier <rtId>`, `--yes` (optional) | Rotate the secret of the adapter's pipeline service account (destructive — prompts) |
| `SwitchServiceAccountsToImpersonation` | `--apply` (optional, default dry run), `--redeployDataFlows <true\|false>` (default true), `--yes`, `--json` | Dry-run or apply the switch of the tenant's pipeline service accounts to impersonation (apply is destructive — prompts) |

#### `RotateAdapterServiceAccountSecret`

Runs the CK method `System.Communication/Adapter.RotateServiceAccountSecret-1` (`RtAdapterMethodClient`, see "Write
commands run the System.Communication methods" above; it needs the role `CommunicationManagement`). Before, it called
`POST {tenantId}/v1/adapter/{adapterRtId}/serviceAccount/rotateSecret`, whose facade runs the same handler. Three
properties of it are deliberate and easy to undo by accident:

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

#### `SwitchServiceAccountsToImpersonation`

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
| `GetLatestPipelineExecution` | `--identifier <rtId>`, `--json` (optional) | Get the most recent pipeline execution; `FailureKind` is shown as `OutcomeUnknown` / `Failed` / `null` |
| `GetPipelineDebugPoints` | `--identifier <rtId>`, `--executionId <guid>`, `--json` (optional) | Get debug points for a specific execution |
| `SetPipelineDebug` | `--identifier <rtId>`, `--enabled <true\|false>` | Enable or disable debug capture for a pipeline |
| `GetPipelineDebug` | `--identifier <rtId>`, `--json` (optional) | Get the debug state of a pipeline |

`GetPipelineExecutions` and `GetLatestPipelineExecution` print the execution JSON through `PipelineExecutionOutput`:
`FailureKind` (octo-sdk `PipelineExecutionDataDto`, kind of the controller) appears as its name - `"OutcomeUnknown"` when a
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

## Agent commands: CreateAgent, UpdateAgent, EnableAgent, DisableAgent, GetAgents, GetAgentRuns

`Ai/Agents/*AgentCommand.cs`, `GetAgentsCommand.cs`, `GetAgentRunsCommand.cs`, shared part `AgentMcp.cs` (AB#8455).

```bash
octo-cli -c CreateAgent -n "Knowledge curator" -if ./curator.md -g "knowledge_*:Medium" -rc Medium [-d] [-r <role>]...
octo-cli -c UpdateAgent -a "Knowledge curator" [-n|-i|-if|-m|-cm|-al|-rk|-su|-mid|-g...|-cg|-rc|-cb|-wm|-ct...|-cct|-r...|-cr]
octo-cli -c EnableAgent  -a "Knowledge curator"
octo-cli -c DisableAgent -a "Knowledge curator"
octo-cli -c GetAgents [-a "Knowledge curator"] [--json]
octo-cli -c GetAgentRuns [-a "Knowledge curator"]
```

- **The checks stay in the MCP service.** Each command calls one agent tool of octo-mcp-service — `create_agent`,
  `update_agent`, `set_agent_enabled`, `list_agents`, `get_agent` — through the MCP client of the knowledge
  platform-update commands (`Services/McpToolClient.cs`, `KnowledgePlatformUpdatesMcp`), with the signed-in user's own
  token (exchanged for another tenant with `-tid`, as every command). The tools allow tenant administrators only (role
  `TenantManagement` or `UserManagement`) and refuse an agent's own token; a refusal stops with the tool's code, e.g.
  `NotTenantAdministrator`. There is no REST endpoint for agents.
- **URL only from the context.** The endpoint is `<McpServiceUrl of the context>/<tenant>/mcp` (`AddContext -msu`,
  `Config -msu`). The agent commands have no per-call URL option, so the token goes only to the URL stored in the context.
- **No client secret on the command line.** `create_agent` generates the secret of the agent's service-account client
  and never returns it; `CreateAgent` has no option for it, so no secret lands in a shell history or process list.
- **Grants.** `-g <toolPattern>[:<maxRisk>[:<argumentConstraints JSON>]]`, repeatable (`knowledge_*:Medium`,
  `query_entities:Low:{"ckTypeId":["System.Ai/KnowledgePage"]}`). In `UpdateAgent` the given grants, CK types (`-ct`) and
  roles (`-r`) replace the list; `-cg`, `-cct`, `-cr` clear it, `-cm` goes back to the tenant default model. Defaults of a
  new agent are narrow: no grants, risk ceiling Low, change budget 0, write mode Propose.
- **GetAgentRuns** is not available yet: it ends with "Agent runs are not available yet" (AB#8460 brings the run
  history), without signing in or calling a service.
- **Output.** One line per agent (name, id, enabled, version, autonomy, client, mandate summary); `GetAgents -a` and the
  write commands add model, runner, sponsor, client state, grants, writable CK types and the first line of the
  instructions. `--json` prints the tool's answer. The token is never logged or printed, at any log level.
- Tests: `AgentCommandsTests` (tool, endpoint and token per command, every option, clear flags, invalid input before any
  call, no URL override, refusal of a non-administrator, GetAgentRuns, token never in logs or output).
