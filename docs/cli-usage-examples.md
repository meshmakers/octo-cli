---
description: Command examples for contexts, login, tenants, blueprints, archives, groups, clients, workloads and AI services.
background: true
---

# Common operations

## Common Operations

```bash
# Context management
octo-cli -c AddContext -n dev -isu https://localhost:5003/ -tid octosystem
octo-cli -c AddContext -n prod -isu https://id.example.com/ -tid customer1
octo-cli -c UseContext -n dev       # Switch to dev context
octo-cli -c UseContext              # List all contexts (no -n arg, legacy shortcut)
octo-cli -c RemoveContext -n prod   # Remove a context
octo-cli -c ListContexts            # Tabular list of all contexts with auth status
octo-cli -c ListContexts -n dev     # Detail view of one context (all service URIs)
octo-cli -c ListContexts -j         # JSON output for scripting (tokens never included)

# Run a single command against another context without switching the active one.
# Works with every command and in any position.
octo-cli --context prod -c GetUsers
octo-cli -c GetAdapters --context prod
export OCTO_CLI_CONTEXT=prod        # same, for a whole subshell
```

Parallel jobs against different environments (this is what the override is for), in PowerShell:

```powershell
foreach ($c in 'test_a','test_b') { Start-Job { octo-cli --context $using:c -c DeployTriggers } }
```

```bash
# Fully isolated configuration per job — no shared contexts.json at all:
OCTO_CLI_HOME=/tmp/job-$JOB_ID octo-cli -c AddContext -n ci -isu … -tid …

# Login via device code flow (tokens saved to the context in use)
octo-cli -c LogIn
octo-cli --context prod -c LogIn    # log into prod without making it active

# Non-disruptive login: reuse the valid token or refresh it silently, and only
# open a browser if neither works (great for re-runnable setup scripts).
octo-cli -c LogIn -i --if-needed

# Non-interactive login (pipelines, cron jobs, headless scripts, etc.)
# Tenant comes from the active context.
export OCTO_CLI_CLIENT_ID=my-client-id
export OCTO_CLI_CLIENT_SECRET=***
octo-cli -c LogInClientCredentials
# Subsequent commands auto-renew the token while the env vars remain exported.

# Check authentication status (tokens are masked; add --show-token to print them)
octo-cli -c AuthStatus

# Configure the active context (rejected when combined with --context)
octo-cli -c Config -isu https://localhost:5003/ -asu https://localhost:5001/ -tid meshtest

# List identity providers
octo-cli -c GetIdentityProviders

# Add Azure Entra ID provider
octo-cli -c AddAzureEntryIdIdentityProvider -n "Azure AD" -e true -t <tenantId> -cid <clientId>   # asks for the client secret

# List users
octo-cli -c GetUsers

# Create tenant (automatically provisions current user as admin)
octo-cli -c Create -tid mytenant -db mytenant

# Create tenant without admin provisioning
octo-cli -c Create -tid mytenant -db mytenant --no-provision

# First administrator of a tenant without users, in a client-credentials session of its creator (asks for the password; default tenant: the context's)
octo-cli -c Setup -e admin@example.com -tid mytenant

# Inspect a tenant's durable provisioning lifecycle (Creating/Active/Deleting/Failed)
octo-cli -c GetTenantLifecycle -tid mytenant
# Operator safety valve: re-open a stuck/Failed tenant's setup so the background reconciler finishes it
octo-cli -c ReRunTenantSetup -tid mytenant

# Show the enabled-state of the active tenant's capabilities (Stream Data, Communication,
# Reporting, AI Services) — the same flags the tenant delete/detach guard evaluates.
# The Stream Data line also notes when stream data is switched off at the instance level.
octo-cli -c GetTenantFeatures

# Blueprints (asset repository) — initial install path is Phase 1
octo-cli -c ListBlueprints                                          # list catalog blueprints across all sources (all pages)
octo-cli -c ListBlueprints -n Samples.Photovoltaics                 # all versions of one blueprint (-s/-t = single page)
octo-cli -c RefreshBlueprintCatalogs                                # force-refresh all blueprint catalog caches at the asset repo
octo-cli -c RefreshBlueprintCatalogs -cn PrivateGitHubBlueprintCatalog  # force-refresh a single catalog (case-insensitive name)
octo-cli -c InstallBlueprint -b MyBlueprint-1.0.0                   # apply blueprint to the active tenant
octo-cli -c InstallBlueprint -b MyBlueprint-1.0.0 -f                # re-apply seed data via upsert (recovery)
octo-cli -c GetBlueprintHistory                                     # show application history for the active tenant

# Blueprints — multi-install + uninstall (Phase 3)
octo-cli -c ListBlueprintInstallations                              # list blueprints currently installed on the tenant
octo-cli -c UninstallBlueprint -n MyBlueprint                       # uninstall a blueprint (locked owned entities erased)
octo-cli -c UninstallBlueprint -n MyBlueprint -c                    # cascade: also remove dependents and orphan deps
octo-cli -c UninstallBlueprint -n MyBlueprint -y                    # skip the confirmation prompt
octo-cli -c UninstallBlueprint -n MyBlueprint --dry-run             # list what would be erased and kept (reason), change nothing

# Blueprints — update (Phase 2a: operations layer; richer diff/merge in Phase 2b)
octo-cli -c PreviewBlueprintUpdate -tv MyBlueprint-2.0.0            # preview changes a target version would apply (Merge mode)
octo-cli -c PreviewBlueprintUpdate -tv MyBlueprint-2.0.0 -m Safe    # preview in Safe mode
octo-cli -c UpdateBlueprint -tv MyBlueprint-2.0.0                   # apply update with Merge mode
octo-cli -c UpdateBlueprint -tv MyBlueprint-2.0.0 -m Full           # apply Full mode
octo-cli -c UpdateBlueprint -tv MyBlueprint-2.0.0 -dr               # dry-run (no persistent changes)
# Tenant upgrade guard: the blueprint's CK models are judged against the installed models
octo-cli -c PreviewBlueprintUpdate -tv MyBlueprint-2.0.0 -fob       # --failOnBlocked: non-zero exit when the CK models would break an installed model (CI gate)
octo-cli -c UpdateBlueprint -tv MyBlueprint-2.0.0 -f                # --force (= --forceBreakingUpdate): proceed although installed models break
octo-cli -c InstallBlueprint -b MyBlueprint-2.0.0 -fbu              # --forceBreakingUpdate (InstallBlueprint -f already means re-apply)

# Blueprint archives: InstallBlueprint and UpdateBlueprint log what happened to the archives of the seed (BlueprintArchiveActivationReport):
# a summary ("3 Activated, 1 SkippedStreamDataDisabled ...; 1 rollup backfill(s) queued"), the backfill job id per new rollup (follow with ListRecomputeJobs),
# a warning per skipped archive and per service warning, an error per failed archive. InstallBlueprint also prints the JSON result with archiveActivations.
# Tenant without stream data: the install succeeds, the archives are left out and a warning says so; re-apply with -f after EnableStreamData.

# Blueprints — blanking protection. A seed with an empty/omitted attribute would
# clear a tenant value; the service keeps it unless confirmed. Summaries only, never values.
octo-cli -c PreviewBlueprintUpdate -tv MyBlueprint-2.0.0            # JSON on stdout; blanking table + hint as warnings (log)
octo-cli -c PreviewBlueprintUpdate -tv MyBlueprint-2.0.0 -fb        # --failOnBlanking: exit -5 when anything would be blanked (CI gate)
octo-cli -c UpdateBlueprint -tv MyBlueprint-2.0.0                   # default: tenant values KEPT, kept attributes listed, exit 0
octo-cli -c UpdateBlueprint -tv MyBlueprint-2.0.0 -cb <rtId>:<attr> -cb <rtId2>:<attr2>  # --confirm-blanking (repeatable): blank exactly these
octo-cli -c UpdateBlueprint -tv MyBlueprint-2.0.0 -ab               # --allow-blanking: blank EVERY listed attribute (loud warning)
# Tenant-owned seed entities (rtBlueprintLocked: false): an edited one is never rewritten by an update;
# one the tenant never edited receives a changed seed. Both commands log (info, not warning) the tables
# 'tenant-owned, refreshed', 'tenant-owned, skipped', 'tenant-owned, update available' (seed changed, content differs from the installed seed,
# subset of skipped) and 'deleted by the tenant, stays deleted' (key, type, rtId or '-');
# nothing when the lists are empty; identity only, no values; --failOnBlanking ignores them. The blanking Reason
# (SeedEmpty | SeedOmitted | ResetToDefault) is printed as the service reports it.

# Stream data lifecycle (asset repository)
octo-cli -c EnableStreamData
octo-cli -c ActivateArchive -id 69fda707d47638c68edc7fea       # provisions per-archive CrateDB table
octo-cli -c DisableArchive -id 69fda707d47638c68edc7fea        # status-only; data preserved
octo-cli -c EnableArchive -id 69fda707d47638c68edc7fea         # back from Disabled to Activated
octo-cli -c RetryArchiveActivation -id 69fda707d47638c68edc7fea # only from Failed
octo-cli -c DeleteArchive -id 69fda707d47638c68edc7fea          # destructive — drops table, lose data
octo-cli -c DeleteArchive -id 69fda707d47638c68edc7fea -y       # skip confirmation
octo-cli -c DisableStreamData

# Archive data export / import — move archive row data between tenants/environments.
# Export the whole archive to a ZIP (starts a bot job, waits, then downloads the result).
octo-cli -c ExportArchiveData -tid mytenant -aid 69fda707d47638c68edc7fea -o ./archive-export.zip
# Export only a half-open time slice [fromUtc, toUtc) (ISO-8601 UTC; omit both for whole archive).
octo-cli -c ExportArchiveData -tid mytenant -aid 69fda707d47638c68edc7fea \
  -from 2026-05-11T00:00:00Z -to 2026-05-12T00:00:00Z -o ./archive-slice.zip
# Import a ZIP into a target archive. The target archive MUST be Disabled during import (§7.1):
#   octo-cli -c DisableArchive -id <archiveRtId>
#   octo-cli -c ImportArchiveData -tid mytenant -aid <archiveRtId> -i ./archive-export.zip -w
#   octo-cli -c EnableArchive -id <archiveRtId>
# Default mode is InsertOnly; use Upsert for windowed (time-range / rollup) archives.
octo-cli -c ImportArchiveData -tid mytenant -aid 69fda707d47638c68edc7fea -i ./archive-export.zip -m Upsert -w
# -w waits for the job to finish and surfaces the bot's error (schema mismatch / archive-not-Disabled) verbatim.

# Rollup archive operations (rollup-archives concept §9)
octo-cli -c ListRollupsForArchive -id <sourceArchiveRtId>       # list rollups attached to a source archive
octo-cli -c FreezeRollupArchive -id <rollupRtId> -u 2026-05-11T14:00:00Z   # set FrozenUntil (monotonic)
octo-cli -c UnfreezeRollupArchive -id <rollupRtId>              # clear FrozenUntil (idempotent)
octo-cli -c UnfreezeRollupArchive -id <rollupRtId> -ag          # accept resulting gaps
octo-cli -c RewindRollupWatermark -id <rollupRtId> -t 2026-05-11T10:00:00Z # re-aggregate from boundary

# Optimistic rollup recompute — recompute a window range with no-mixed-read swap
octo-cli -c RecomputeArchive -id <rollupRtId> -f 2026-05-11T00:00:00Z -t 2026-05-12T00:00:00Z   # recompute [from, to)
octo-cli -c RecomputeArchive -id <rollupRtId> -f 2026-05-11T00:00:00Z -t 2026-05-12T00:00:00Z -s <rtId>  # scoped to one entity
octo-cli -c ListRecomputeJobs -id <rollupRtId>                 # recent recompute jobs (debug failures)
# Backfill a rollup over the ENTIRE history of its source archive — no timestamp needed.
# Durable background job: enqueues a recompute of [sourceMin, now) and returns the Pending job id
# immediately (never cancelled by a client HTTP timeout; survives an asset-repo restart).
octo-cli -c BackfillRollup -id <rollupRtId>                    # queue whole-history backfill, return job id
octo-cli -c BackfillRollup -id <rollupRtId> -w                # queue, then poll the job until it completes

# Ingested column — add an attribute column to an Activated archive; history kept, existing rows read null
octo-cli -c AddArchiveColumn -id <archiveRtId> -p DischargingPower          # -x true to index it; idempotent
# A blueprint update / ImportRt whose seed adds columns to an Activated archive adds them the same way
# (computed ones via the backfill job) and lists them in the update result (archiveColumnsAdded).

# Computed columns — add a formula-derived column to a live archive and backfill it
octo-cli -c AddComputedColumn -id <archiveRtId> -n powerFactor -f "activePower / apparentPower" -r Double
octo-cli -c AddComputedColumn -id <archiveRtId> -n flag -f "voltage > 230" -r Boolean -x false  # not indexed
octo-cli -c RemoveComputedColumn -id <archiveRtId> -n powerFactor    # rejected if another computed column references it
octo-cli -c UpdateComputedColumnFormula -id <archiveRtId> -n powerFactor -f "activePower / (apparentPower + 1)"  # optimistic backfill + atomic swap

# Groups management
octo-cli -c GetGroups
octo-cli -c CreateGroup -n "MyGroup" -d "Description" -rids "role1,role2"
octo-cli -c UpdateGroup -id <groupId> -n "NewName"
octo-cli -c DeleteGroup -id <groupId>
octo-cli -c UpdateGroupRoles -id <groupId> -rids "role1,role2"
octo-cli -c AddUserToGroup -id <groupId> -uid <userId>
octo-cli -c RemoveUserFromGroup -id <groupId> -uid <userId>
octo-cli -c AddGroupToGroup -id <parentGroupId> -cgid <childGroupId>
octo-cli -c RemoveGroupFromGroup -id <parentGroupId> -cgid <childGroupId>

# Client role / group assignment — lets a client_credentials client act as a
# role-protected caller. Roles flow into the client's access token (direct + group-inherited).
octo-cli -c AddClientToRole -id ci-deploy -r DataAnalyst          # assign a role by name
octo-cli -c RemoveClientFromRole -id ci-deploy -r DataAnalyst     # remove a role (-y to skip confirm)
octo-cli -c UpdateClientRoles -id ci-deploy -rids "660...02,660...09"  # replace-all by role id
octo-cli -c AddClientToGroup -id <groupRtId> -cid <clientRtId>    # add client (by RtId) to a group
octo-cli -c RemoveClientFromGroup -id <groupRtId> -cid <clientRtId>

# Email domain group rules
octo-cli -c GetEmailDomainGroupRules
octo-cli -c CreateEmailDomainGroupRule -edp "meshmakers.com" -tgid <groupRtId>
octo-cli -c UpdateEmailDomainGroupRule -id <ruleId> -edp "meshmakers.com" -tgid <groupRtId>
octo-cli -c DeleteEmailDomainGroupRule -id <ruleId>

# External tenant user mappings
octo-cli -c GetExternalTenantUserMappings -stid <sourceTenantId>
octo-cli -c CreateExternalTenantUserMapping -stid <sourceTenantId> -suid <sourceUserId> -sun <sourceUserName>
octo-cli -c UpdateExternalTenantUserMapping -id <mappingId> -rids "role1,role2"
octo-cli -c DeleteExternalTenantUserMapping -id <mappingId>

# Admin provisioning (run from system tenant context)
octo-cli -c GetAdminProvisioningMappings -ttid <targetTenantId>
octo-cli -c CreateAdminProvisioningMapping -ttid <targetTenantId> -stid <sourceTenantId> -suid <sourceUserId> -sun <sourceUserName>
octo-cli -c ProvisionCurrentUser -ttid <targetTenantId>
octo-cli -c DeleteAdminProvisioningMapping -ttid <targetTenantId> -mid <mappingId>

# CI/CD workload rollout — chart-version staging + deploy.
# List every workload in the active tenant that uses the chart.
octo-cli -c GetWorkloadsByChart -cn octo-mesh-adapter
# Set ChartVersion on a workload. Does NOT trigger a deploy.
octo-cli -c UpdateWorkloadChartVersion -id <workloadRtId> -cv 1.2.3
# Show / set / clear the typed CPU and memory settings of an adapter or application
# (System.Communication 4.10.0). Does NOT deploy. A flag without a value clears it.
# Reads and writes through the tenant GraphQL endpoint (the controller has no endpoint for them),
# checks quantities and request <= limit locally first. GetAdapter shows them under "resources".
# Precedence at deploy: chart default < these settings < explicit valuesYaml / values[].
octo-cli -c UpdateWorkloadResources -id <workloadRtId>
octo-cli -c UpdateWorkloadResources -id <workloadRtId> -mr 512Mi -ml 1Gi -cr 250m
octo-cli -c UpdateWorkloadResources -id <workloadRtId> -ml
# Trigger deploy of the workload through its parent pool.
octo-cli -c DeployWorkload -id <workloadRtId>
# Undeploy (destructive — confirmation prompt; -y to skip).
octo-cli -c UndeployWorkload -id <workloadRtId>

# Pipeline reassignment — move pipelines onto a different adapter when a
# fresh Blueprint provisioned a replacement adapter. Source + target adapter
# must share the same CkTypeId. Each pipeline is moved atomically;
# per-pipeline failures are listed in the output but don't abort the batch.
octo-cli -c MovePipelines -ids p1,p2,p3 -aid <newAdapterRtId>
# Add -rd to also redeploy each moved pipeline onto the new adapter.
# A redeploy failure does not roll the move back — the pipeline already
# points at the new adapter, just hit DeployPipeline manually once the
# adapter is back online.
octo-cli -c MovePipelines -ids p1,p2 -aid <newAdapterRtId> -rd
# -y skips the interactive confirmation prompt.
octo-cli -c MovePipelines -ids p1 -aid <newAdapterRtId> -y

# Rotate the secret of an adapter's pipeline service account.
# Destructive: the old secret stops working the moment the controller answers.
# A blueprint cannot do this — the secret attribute is runtime state — so this is the path.
octo-cli -c RotateAdapterServiceAccountSecret -id <adapterRtId>
octo-cli -c RotateAdapterServiceAccountSecret -id <adapterRtId> -y   # skip confirmation
# ...then redeploy, or the pipelines keep presenting the withdrawn secret:
octo-cli -c DeployDataFlow -id <dataFlowRtId>

# Impersonation switch of the tenant's pipeline service accounts: dry run by default.
octo-cli -c SwitchServiceAccountsToImpersonation                 # dry run, changes nothing
octo-cli -c SwitchServiceAccountsToImpersonation -a -y -rdf true # apply, no prompt

# Multi-tenant ClientCredentials mirroring
# Create a flagged client in octosystem — gets auto-provisioned into every new sub-tenant.
vault kv get -field=secret <path> | octo-cli -c AddClientCredentialsClient -id ci-deploy -n "CI Deploy" --secret-stdin -apic
# List the sub-tenants a client has been mirrored into.
octo-cli -c GetClientMirrors -id ci-deploy
# Backfill: provision the flagged client into every existing sub-tenant (idempotent).
octo-cli -c ProvisionClientInExistingTenants -id ci-deploy
# Manually provision into a single named sub-tenant.
octo-cli -c ProvisionClientInTenant -id ci-deploy -ctid acme
# Manually remove a mirror (destructive — confirmation prompt, -y to skip).
octo-cli -c UnprovisionClientFromTenant -id ci-deploy -ctid acme
# Flip the AutoProvisionInChildTenants flag on an existing client.
# Note: flipping true does NOT auto-backfill — use ProvisionClientInExistingTenants for that.
octo-cli -c SetClientAutoProvision -id ci-deploy -e true

# Client overlay URIs — append operator-scoped URIs onto blueprint-managed
# clients (e.g. local-dev callbacks on the Refinery Studio client) without modifying the
# blueprint. Entries are written with Source = "overlay:<OverlayName>" and survive blueprint
# re-apply via the Step 2a preservation pass. Idempotent — duplicates silently skipped.
octo-cli -c ApplyClientOverlay -id octo-data-refinery-studio -n local-dev \
  -r "http://localhost:4200/auth-callback,http://localhost:4200/silent-callback" \
  -plr "http://localhost:4200/" \
  -co "http://localhost:4200"

# Strip overlay URIs — destructive cleanup before a sanitised tenant dump.
# Without -n: removes every overlay:* source. With -n: removes only overlay:<name>.
# Typical workflow before sharing a dump:
#   octo-cli -c CleanClientOverlays -y && octo-cli -c DumpTenant -tid mytenant ...
# After dumping, restore the local-dev overlays via the cmdlet (idempotent):
#   Invoke-IdentityOverlay
octo-cli -c CleanClientOverlays                         # strip all overlay:* (prompts)
octo-cli -c CleanClientOverlays -n local-dev -y         # strip only overlay:local-dev, skip prompt

# OctoTenant identity provider (cross-tenant auth)
octo-cli -c AddOctoTenantIdentityProvider -n "ParentTenant" -e true -ptid <parentTenantId>

# Identity providers with self-registration and default group
octo-cli -c AddAzureEntryIdIdentityProvider -n "Azure" -e true -t <tenantId> -cid <clientId> -asr false -dgid <groupRtId>   # asks for the client secret

# AI Services — tenant lifecycle (run after EnableCommunication)
octo-cli -c EnableAi
octo-cli -c DisableAi

# AI credential bastion flow — register an Anthropic subscription token on a tenant.
#   Two-machine pattern: a tenant admin mints a one-time code via Refinery Studio
#   ("Issue ticket" panel on the AI Console). The code is handed out-of-band to the
#   operator on the bastion machine, who has just completed `claude /login`. The
#   operator then redeems the code together with the freshly minted Anthropic
#   tokens — anonymously, no OctoMesh user session required. The code is the auth
#   artefact; it can be redeemed exactly once and expires after a few minutes.
#   The tokens come from the environment (the -at / -rt arguments are deprecated).
read -rs OCTO_CLI_AI_ACCESS_TOKEN && read -rs OCTO_CLI_AI_REFRESH_TOKEN && export OCTO_CLI_AI_ACCESS_TOKEN OCTO_CLI_AI_REFRESH_TOKEN
octo-cli -c RedeemAiTicket \
  -tid meshtest \
  -tc TWUL9NMV7LU8 \
  -aex 2027-01-01T00:00:00Z \
  -rex 2027-12-31T00:00:00Z

# Inspect or revoke the active tenant's lease — both require an authenticated
# tenant-scoped session (current context's TenantId + AiServiceUrl must be set).
octo-cli -c GetAiCredentialsStatus
octo-cli -c RevokeAiCredentials      # destructive — prompts for confirmation
octo-cli -c RevokeAiCredentials -y   # skip confirmation
```
