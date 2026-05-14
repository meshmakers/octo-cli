using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Blueprints;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Blueprints;

internal class UpdateBlueprint : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IArgument _targetVersionArg;
    private readonly IArgument _updateModeArg;
    private readonly IArgument _noBackupArg;
    private readonly IArgument _dryRunArg;

    public UpdateBlueprint(
        ILogger<UpdateBlueprint> logger,
        IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "UpdateBlueprint",
            "Applies a blueprint update to the active tenant.",
            options, assetServicesClient, authenticationService)
    {
        _targetVersionArg = CommandArgumentValue.AddArgument("tv", "targetVersion",
            ["Fully-qualified target blueprint id, e.g. 'MyBlueprint-2.0.0'"], true, 1);

        _updateModeArg = CommandArgumentValue.AddArgument("m", "updateMode",
            ["Update mode: Safe, Merge (default), Full, or Migration"], false, 1);

        _noBackupArg = CommandArgumentValue.AddArgument("nb", "no-backup",
            ["Skip the pre-update tenant backup (not recommended)"], false, 0);

        _dryRunArg = CommandArgumentValue.AddArgument("dr", "dry-run",
            ["Simulate the update without persisting changes"], false, 0);
    }

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing - configure it via the active context");
            return;
        }

        var targetVersion = CommandArgumentValue.GetArgumentScalarValue<string>(_targetVersionArg);
        var updateMode = CommandArgumentValue.IsArgumentUsed(_updateModeArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_updateModeArg)
            : "Merge";
        var createBackup = !CommandArgumentValue.IsArgumentUsed(_noBackupArg);
        var dryRun = CommandArgumentValue.IsArgumentUsed(_dryRunArg);

        Logger.LogInformation(
            "Applying update of tenant '{TenantId}' to '{TargetVersion}' (mode={UpdateMode}, backup={Backup}, dryRun={DryRun})",
            Options.Value.TenantId, targetVersion, updateMode, createBackup, dryRun);

        var request = new BlueprintUpdateRequestDto
        {
            TargetVersion = targetVersion,
            UpdateMode = updateMode,
            CreateBackup = createBackup,
            DryRun = dryRun
        };

        await ServiceClient.ApplyBlueprintUpdateAsync(Options.Value.TenantId, request);

        Logger.LogInformation("Blueprint update applied to tenant '{TenantId}'", Options.Value.TenantId);
    }
}
