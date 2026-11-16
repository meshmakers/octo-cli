using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Blueprints;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Blueprints;

internal class UninstallBlueprint : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _blueprintNameArg;
    private readonly IArgument _cascadeArg;
    private readonly IArgument _yesArg;
    private readonly IArgument _dryRunArg;

    public UninstallBlueprint(
        ILogger<UninstallBlueprint> logger,
        IConsoleService consoleService,
        IConfirmationService confirmationService,
        IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "UninstallBlueprint",
            "Removes a blueprint from the active tenant; with --cascade, dependents and orphan dependencies go too. " +
            "Erases the locked entities and the tenant-owned seed entities the tenant never edited; edited or referenced ones " +
            "stay and are listed with the reason (tenantOwnedKept). --dry-run shows what would be erased and kept, changing nothing. " +
            "Requires the tenant role DataModelManagement (TenantOwners carry it); otherwise 403 DataModelManagementRequired.",
            options, assetServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _confirmationService = confirmationService;

        _blueprintNameArg = CommandArgumentValue.AddArgument("n", "blueprintName",
            ["Blueprint name (without version), e.g. 'MyBlueprint'"], true, 1);

        _cascadeArg = CommandArgumentValue.AddArgument("c", "cascade",
            ["Also uninstall blueprints that depend on the target, and orphan dependencies of the target"],
            false, 0);

        _yesArg = CommandArgumentValue.AddArgument("y", "yes",
            ["Skip the interactive confirmation prompt"], false, 0);

        _dryRunArg = CommandArgumentValue.AddArgument("dr", "dry-run",
            [
                "Change nothing: list the entities the uninstall would erase and the tenant-owned seed entities it would keep,",
                "with the reason (Edited, Referenced, NotComparable, EraseFailed). A 404 without a message means the asset repository predates AB#8966."
            ], false, 0);
    }

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing - configure it via the active context");
            return;
        }

        var blueprintName = CommandArgumentValue.GetArgumentScalarValue<string>(_blueprintNameArg);
        var cascade = CommandArgumentValue.IsArgumentUsed(_cascadeArg);
        var skipConfirmation = CommandArgumentValue.IsArgumentUsed(_yesArg);
        var dryRun = CommandArgumentValue.IsArgumentUsed(_dryRunArg);

        if (dryRun)
        {
            // Nothing is changed, so no confirmation is asked.
            Logger.LogInformation(
                "Dry run: uninstalling blueprint '{BlueprintName}' from tenant '{TenantId}' (cascade={Cascade}) - nothing is changed",
                blueprintName, Options.Value.TenantId, cascade);
            var preview = await ServiceClient.PreviewUninstallBlueprintAsync(Options.Value.TenantId, blueprintName, cascade);
            Report(blueprintName, preview);
            return;
        }

        var promptDetail = cascade
            ? $" together with any blueprints that depend on it and any orphaned dependencies"
            : string.Empty;

        if (!skipConfirmation && !_confirmationService.Confirm(
                $"uninstall blueprint '{blueprintName}' from tenant '{Options.Value.TenantId}'{promptDetail}? Locked owned entities and unedited tenant-owned seed entities will be erased (see --dry-run)"))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation(
            "Uninstalling blueprint '{BlueprintName}' from tenant '{TenantId}' (cascade={Cascade})",
            blueprintName, Options.Value.TenantId, cascade);

        var result = await ServiceClient.UninstallBlueprintAsync(
            Options.Value.TenantId, blueprintName, cascade);
        Report(blueprintName, result);
    }

    private void Report(string blueprintName, BlueprintUninstallResultDto result)
    {
        var verb = result.DryRun ? "would be" : "were";
        if (!result.Success)
        {
            if (result.BlockingDependents.Count > 0)
            {
                Logger.LogError(
                    "Uninstall blocked: '{BlueprintName}' is still required by {Dependents}. Re-run with --cascade to remove them as well.",
                    blueprintName, string.Join(", ", result.BlockingDependents));
            }
            else
            {
                Logger.LogError("Uninstall {Mode} failed", result.DryRun ? "dry run" : "run");
            }

            _consoleService.WriteLine(JsonConvert.SerializeObject(result, Formatting.Indented));
            // After the report, so scripts see a non-zero exit code (the result JSON is already printed).
            throw new ToolException(result.BlockingDependents.Count > 0
                ? $"Uninstall {(result.DryRun ? "dry run " : string.Empty)}of '{blueprintName}' blocked by {string.Join(", ", result.BlockingDependents)}"
                : $"Uninstall {(result.DryRun ? "dry run " : string.Empty)}of '{blueprintName}' failed");
        }

        if (result.DryRun)
        {
            Logger.LogInformation(
                "Uninstall dry run: {EntitiesDeleted} entities would be erased, {KeptCount} tenant-owned seed entities kept, {CascadedCount} cascaded blueprints",
                result.EntitiesDeleted, result.TenantOwnedKept.Count, result.CascadedDependencies.Count);
        }
        else
        {
            Logger.LogInformation(
                "Uninstall complete: {EntitiesDeleted} entities erased, {KeptCount} tenant-owned seed entities kept, {CascadedCount} cascaded blueprints",
                result.EntitiesDeleted, result.TenantOwnedKept.Count, result.CascadedDependencies.Count);
        }

        foreach (var kept in result.TenantOwnedKept)
        {
            Logger.LogInformation("  kept ({Reason}): {CkTypeId} {Key} {RtId}{ReferencedBy}",
                kept.Reason, kept.CkTypeId, kept.Key, kept.EntityId ?? "-",
                kept.ReferencedBy.Count > 0 ? $" referenced by {string.Join(", ", kept.ReferencedBy)}" : string.Empty);
        }

        if (result.DeletedEntities.Count > 0)
        {
            Logger.LogInformation("{Count} entities {Verb} erased (deletedEntities in the JSON below)",
                result.DeletedEntities.Count, verb);
        }

        _consoleService.WriteLine(JsonConvert.SerializeObject(result, Formatting.Indented));
    }
}
