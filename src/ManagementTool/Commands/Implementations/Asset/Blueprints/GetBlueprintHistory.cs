using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Blueprints;

internal class GetBlueprintHistory : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IConsoleService _consoleService;

    public GetBlueprintHistory(
        ILogger<GetBlueprintHistory> logger,
        IConsoleService consoleService,
        IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "GetBlueprintHistory",
            "Shows the blueprint application history for the current tenant.", options,
            assetServicesClient, authenticationService)
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
            "Fetching blueprint history for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            Options.Value.TenantId, ServiceClient.ServiceUri);

        var history = await ServiceClient.GetBlueprintHistoryAsync(Options.Value.TenantId);

        if (history.Count == 0)
        {
            Logger.LogInformation("No blueprint has ever been applied to tenant '{TenantId}'",
                Options.Value.TenantId);
            return;
        }

        var resultString = JsonConvert.SerializeObject(history, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }
}
