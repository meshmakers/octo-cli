using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Blueprints;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Blueprints;

internal class PreviewBlueprintUpdate : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _targetVersionArg;
    private readonly IArgument _updateModeArg;

    public PreviewBlueprintUpdate(
        ILogger<PreviewBlueprintUpdate> logger,
        IConsoleService consoleService,
        IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "PreviewBlueprintUpdate",
            "Previews the changes a blueprint update would make without applying them.",
            options, assetServicesClient, authenticationService)
    {
        _consoleService = consoleService;

        _targetVersionArg = CommandArgumentValue.AddArgument("tv", "targetVersion",
            ["Fully-qualified target blueprint id, e.g. 'MyBlueprint-2.0.0'"], true, 1);

        _updateModeArg = CommandArgumentValue.AddArgument("m", "updateMode",
            ["Update mode: Safe, Merge (default), Full, or Migration"], false, 1);
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

        Logger.LogInformation(
            "Previewing update of tenant '{TenantId}' to '{TargetVersion}' (mode={UpdateMode})",
            Options.Value.TenantId, targetVersion, updateMode);

        var request = new BlueprintUpdateRequestDto
        {
            TargetVersion = targetVersion,
            UpdateMode = updateMode,
            DryRun = true
        };

        var preview = await ServiceClient.PreviewBlueprintUpdateAsync(Options.Value.TenantId, request);

        var resultString = JsonConvert.SerializeObject(preview, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }
}
