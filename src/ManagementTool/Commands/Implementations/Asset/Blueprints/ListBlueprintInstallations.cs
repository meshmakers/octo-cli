using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Blueprints;

internal class ListBlueprintInstallations : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IConsoleService _consoleService;

    public ListBlueprintInstallations(
        ILogger<ListBlueprintInstallations> logger,
        IConsoleService consoleService,
        IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "ListBlueprintInstallations",
            "Lists all blueprints currently installed on the active tenant.",
            options, assetServicesClient, authenticationService)
    {
        _consoleService = consoleService;
    }

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing - configure it via the active context");
            return;
        }

        Logger.LogInformation(
            "Listing blueprint installations for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            Options.Value.TenantId, ServiceClient.ServiceUri);

        var installations = await ServiceClient.ListBlueprintInstallationsAsync(Options.Value.TenantId);

        if (installations.Count == 0)
        {
            Logger.LogInformation("No blueprints installed on tenant '{TenantId}'", Options.Value.TenantId);
            return;
        }

        var resultString = JsonConvert.SerializeObject(installations, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }
}
