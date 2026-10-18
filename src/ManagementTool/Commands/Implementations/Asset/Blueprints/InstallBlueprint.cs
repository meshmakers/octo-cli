using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Blueprints;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.CkModelCatalog;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Blueprints;

internal class InstallBlueprint : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _blueprintIdArg;
    private readonly IArgument _forceArg;
    private readonly IArgument _forceBreakingUpdateArg;

    public InstallBlueprint(
        ILogger<InstallBlueprint> logger,
        IConsoleService consoleService,
        IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "InstallBlueprint",
            "Installs a blueprint into the current tenant. CK models are loaded and seed data is imported via upsert.",
            options, assetServicesClient, authenticationService)
    {
        _consoleService = consoleService;

        _blueprintIdArg = CommandArgumentValue.AddArgument("b", "blueprintId",
            ["Fully-qualified blueprint id, e.g. 'MyBlueprint-1.0.0'"], true, 1);

        _forceArg = CommandArgumentValue.AddArgument("f", "force",
            ["Re-apply seed data via upsert even if the same version is already recorded (recovery)"],
            false, 0);

        _forceBreakingUpdateArg = CommandArgumentValue.AddArgument("fbu", "forceBreakingUpdate",
            [
                "Install although the CK models of the blueprint would break an installed CK model (tenant upgrade guard).",
                "Without it the service refuses with CK_UPGRADE_BLOCKED before the first change. Needs the data model management role."
            ],
            false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(
                    arguments: [new CodeSampleArgument(_blueprintIdArg, "Eda.Adapter-1.2.0")],
                    description: "Install a blueprint; refused when its CK models would break an installed model"),
                new CodeSample(
                    arguments:
                    [
                        new CodeSampleArgument(_blueprintIdArg, "Eda.Adapter-1.2.0"),
                        new CodeSampleArgument(_forceBreakingUpdateArg)
                    ],
                    description: "Install anyway; the broken models become ResolveFailed"),
            ],
            Notes:
            [
                "The tenant upgrade guard judges the CK models the blueprint installs or raises as one set against the installed models (same rules and output as ImportFromCatalog).",
                "--force re-applies the seed data of an already installed version; it does not override the guard. Use --forceBreakingUpdate for that.",
                "A refused install changes nothing: no CK model, no installation record, no seed data."
            ]);

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing - configure it via the active context");
            return;
        }

        var blueprintId = CommandArgumentValue.GetArgumentScalarValue<string>(_blueprintIdArg);
        var force = CommandArgumentValue.IsArgumentUsed(_forceArg);
        var forceBreakingUpdate = CommandArgumentValue.IsArgumentUsed(_forceBreakingUpdateArg);

        Logger.LogInformation(
            "Installing blueprint '{BlueprintId}' into tenant '{TenantId}' at '{ServiceClientServiceUri}' (force={Force})",
            blueprintId, Options.Value.TenantId, ServiceClient.ServiceUri, force);

        BlueprintApplyResultDto result;
        try
        {
            result = await ServiceClient.ApplyBlueprintAsync(Options.Value.TenantId,
                new BlueprintApplyRequestDto
                {
                    BlueprintId = blueprintId, Force = force, ForceBreakingUpdate = forceBreakingUpdate
                });
        }
        catch (CkUpgradeBlockedException blocked)
        {
            throw BlueprintUpgradeGuardReport.Refused(Logger, "installation", blueprintId, blocked,
                BlueprintUpgradeGuardReport.ForceOption);
        }

        if (!result.Success)
        {
            Logger.LogError("Blueprint installation failed");
            return;
        }

        BlueprintUpgradeGuardReport.LogForced(Logger, BlueprintUpgradeGuardReport.ForceOption, result.Blockers);

        Logger.LogInformation(
            "Blueprint '{BlueprintId}' applied to tenant '{TenantId}' (mode={Mode}, seedFiles={SeedFiles})",
            result.BlueprintId, result.TenantId, result.ApplicationMode, result.SeedDataFilesApplied);

        var resultString = JsonConvert.SerializeObject(result, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }
}
