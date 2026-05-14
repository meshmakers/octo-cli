using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
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
    }

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing - configure it via the active context");
            return;
        }

        var blueprintId = CommandArgumentValue.GetArgumentScalarValue<string>(_blueprintIdArg);
        var force = CommandArgumentValue.IsArgumentUsed(_forceArg);

        Logger.LogInformation(
            "Installing blueprint '{BlueprintId}' into tenant '{TenantId}' at '{ServiceClientServiceUri}' (force={Force})",
            blueprintId, Options.Value.TenantId, ServiceClient.ServiceUri, force);

        var result = await ServiceClient.ApplyBlueprintAsync(Options.Value.TenantId, blueprintId, force);

        if (!result.Success)
        {
            Logger.LogError("Blueprint installation failed");
            return;
        }

        Logger.LogInformation(
            "Blueprint '{BlueprintId}' applied to tenant '{TenantId}' (mode={Mode}, seedFiles={SeedFiles})",
            result.BlueprintId, result.TenantId, result.ApplicationMode, result.SeedDataFilesApplied);

        var resultString = JsonConvert.SerializeObject(result, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }
}
