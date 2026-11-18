---
description: Tenant upgrade guard for catalog, file and blueprint imports, CK library status, empty-model checks.
applies_to: **/Asset/{CkModelLibraries,Models,Blueprints}/**, CkModelFileInspector.cs
---

# CK models, library status and blueprints

## CK model import and the tenant upgrade guard

`ImportFromCatalog` and `FixAll` (`Commands/Implementations/Asset/CkModelLibraries/`) import through `ResolveDependenciesBatchAsync` +
`ImportFromCatalogBatchAsync`. The asset repository judges the whole import set with the tenant upgrade guard and refuses a set that
would break an installed model with `409 CK_UPGRADE_BLOCKED` (SDK: `CkUpgradeBlockedException`, no partial import).

- **`ImportFromCatalog`**: when `ResolveDependenciesBatch` reports `IsBlocked`, the command prints every blocker (dependent, kind, pin,
  resolve hint via `UpgradeGuardReport`) and stops **before** the import with a `ToolException` that names `--force`. `-f/--force` sends
  `ForceBreakingUpdate = true` (the service logs the blockers and the caller at WARN) and warns first; the help text names the
  consequence (the broken models become `ResolveFailed`). A 409 from a service that did not preview the blockers is reported the same way.
- **`FixAll` never forces** (`ForceBreakingUpdate` is always `false`; there is no force option). Blocked `LibraryStatus` rows (they have
  `NeedsAction = false`) are skipped and printed with reason and resolve hint. If the resolved import set is blocked through a dependency,
  the requested models whose tree contains a blocking update are dropped and reported, the set is resolved again and the rest is imported;
  when no model can be left out, or the service refuses anyway, it fails with `ToolException` ("never forces") and imports nothing.
  A blocker can disappear after another update was imported, so `FixAll` may have to be run twice (update `Registration` first).
- **Joint import of mutually exact-pinned models**: `ImportFromCatalog -m A,B,C` (comma-separated, like `UpdateClientRoles -rids`)
  sends one `ResolveDependenciesBatch` with all roots and one `ImportFromCatalogBatch`; the service judges the list as one post-state, so
  exact pins of replaced versions stop blocking. The service imports ids in request order and concatenates the trees of the roots, so
  `JointImportPlanner.OrderByDependencies` orders the set in the CLI: a model follows every other model of the set whose name appears in
  its dependency tree (stable, resolver order for ties). A blocked import prints a ready-to-run joint command
  (`JointImportPlanner.BuildJointCommand`: requested models + every `suggestedDependentUpdate` of the blockers not yet in the set);
  `--force` keeps its meaning for the whole set. `FixAll` adds blocked rows with a compatible catalog update to the candidate set; the
  existing drop loop leaves out only what is still blocked in the joint set. Tests: `JointImportCommandTests` (octo-mesh-energy group of six).
- Tests: `tests/ManagementTool.Tests/CkUpgradeGuardImportCommandTests.cs`.

## ImportCk rejects empty and missing construction kit files

`ImportCk` (`Commands/Implementations/Asset/Models/ImportConstructionKitModel.cs`) checks the file with
`Services/CkModelFileInspector` before anything is uploaded. A CK model is **empty** when none of the top-level
keys `types`, `records`, `enums`, `attributes`, `associationRoles`, `interfaces` carries an entry (e.g. a file
with only `modelId` and `dependencies`). An empty model raises `ToolException.EmptyCkModel` — a clear message,
non-zero exit code, no request to the server. The check reads `.yaml`/`.yml`/`.json` files and the YAML/JSON
entries of a `.zip` (a zip is rejected only when every model entry in it is empty) with a light top-level scan,
without a YAML dependency; files that cannot be classified are passed through and judged by the server.
A **non-existent input file** also fails with `ToolException.FilePathDoesNotExist` (non-zero exit, no server call) —
it used to log an error and exit 0.
Tests: `tests/ManagementTool.Tests/CkModelFileInspectorTests.cs`, `ImportConstructionKitModelTests.cs`.

## CK library status: ACTION terms and the upgrade guard

`LibraryStatus` derives the ACTION column per row, first match wins: `Service-Managed`, `Incompatible`
(incompatible with the installed System models), **`Blocked`**, `Update` / `Fix` (needs action), `Install`, `-`.

- **`Blocked`** means the offered catalog version would break an installed dependent (tenant upgrade guard). The server row has `IsBlocked`, `HasUpdate = true`, `NeedsAction = false`; therefore
  **`--needs-action` does not list blocked rows** (it follows the server flag). REASON shows the first blocker
  (`<dependent>: <message>`), `(+N more)` and the resolve hint of the first blocker when the server proved one.
- The summary line appends `, N blocked` only when N > 0; below the table a **`Would break:`** section lists every
  blocker of every shown blocked row (dependent, pin/element message, `resolve:` hint).
- `CheckUpgrade` appends a `Would break:` section (one line per blocker) after the unchanged fields only when
  `IsBlocked`. **The exit code is unchanged** (0): both commands stay read-only reports; an unblocked update and an
  older server without the members (`IsBlocked`/`Blockers` default to `false`/empty) print exactly as before.
  A non-zero exit of `CheckUpgrade` was considered and left out (exit code unchanged); it stays an open point (possible later
  pre-flight switch).
- Rendering lives in `UpgradeBlockerFormatter` (null-safe). The commands print through the logger; there is no
  JSON output mode for these two commands. The SDK DTOs carry the new members, so MCP/GraphQL consumers see them.
- Tests: `LibraryStatusCommandTests`, `CheckUpgradeCommandTests`. The command reference page is generated from the
  `GetDocumentation()` samples.

### Tenant upgrade guard for the file import (CK v2 F2.5)

`ImportCk -f <file>` is guarded by the service like `ImportFromCatalog`: a file that would break an installed model - a `System` file
included - is refused with `409 CK_UPGRADE_BLOCKED`, nothing is imported. The command calls the additive SDK overload
`ImportCkModelAsync(tenantId, path, forceBreakingUpdate)`, logs the blockers (`UpgradeGuardReport`, level Error), and fails with a
`ToolException` whose message carries `CK_UPGRADE_BLOCKED` (non-zero exit code, as for `ImportFromCatalog`).

- **Override**: `--forceBreakingUpdate` (short `-fbu`, the same name as `InstallBlueprint` / `UpdateBlueprint`; `-f` is the file here, so
  `--force` was not reused). It logs a WARN and needs the data model management role (403 otherwise). A CLI without the option against an
  older service behaves as before (the older service does not guard); a new CLI against an older service sends a form member that is ignored.
- **CheckUpgrade** help now says that "Breaking Changes" is the migration view of the model itself and "Would break" (`isBlocked` /
  `blockers`) is the tenant upgrade guard; the service judges `System` too (before, `CheckUpgrade -m System-2.8.0` printed
  `Breaking Changes: False` and no blockers for a release that made the tenant unloadable).
- Tests: `ImportConstructionKitModelTests` (refused -> blockers printed + exit error, override sent + WARN, empty/missing file unchanged).

### Tenant upgrade guard for blueprints (CK v2 F2.5)

`InstallBlueprint`, `UpdateBlueprint` and `PreviewBlueprintUpdate` carry the guard of `ImportFromCatalog` to blueprints:
the CK models a blueprint installs or raises are judged against the installed models by the asset repository.

- `PreviewBlueprintUpdate` prints the unchanged JSON (now with `isBlocked`, `ckUpdateSet`, `blockers`) and, when blocked, a warning
  with every blocker (dependent, pin/element, update, resolve hint - the same lines as `ImportFromCatalog`, via
  `UpgradeGuardReport`). `--failOnBlocked` (`-fob`) ends with a `ToolException` for CI gates; without it the exit code is unchanged.
- `UpdateBlueprint --dry-run` lists the blockers as a warning and states that nothing was changed. A real update that the service
  refuses (`CkUpgradeBlockedException`, HTTP 409 `CK_UPGRADE_BLOCKED`) logs the blockers as errors and ends with a `ToolException`
  that names the override. `--force` (`-f`, alias `--forceBreakingUpdate` / `-fbu`) sends `forceBreakingUpdate`; the forced blockers
  of the result are printed as a warning (they become `ResolveFailed`). The caller needs the data model management role (403 otherwise).
- `InstallBlueprint`: `--forceBreakingUpdate` (`-fbu`). **`--force` keeps its meaning (re-apply seed data) and never overrides the
  guard** - that is why the override has a different name here than on `ImportFromCatalog` / `UpdateBlueprint`.
- Against a service without the guard the new DTO members are absent (`IsBlocked` false, `Blockers` empty) and nothing extra is printed.
- Tests: `BlueprintUpgradeGuardCommandTests`. The command reference pages are generated from the command classes.
