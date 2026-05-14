using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Blueprints;

internal class ListBlueprints : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IConsoleService _consoleService;

    public ListBlueprints(
        ILogger<ListBlueprints> logger,
        IConsoleService consoleService,
        IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "ListBlueprints",
            "Lists blueprints available across configured catalogs.", options,
            assetServicesClient, authenticationService)
    {
        _consoleService = consoleService;
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Listing blueprints from '{ServiceClientServiceUri}'", ServiceClient.ServiceUri);

        var result = await ServiceClient.ListBlueprintsAsync();

        if (result.Items.Count == 0)
        {
            Logger.LogInformation("No blueprints found in any catalog");
            return;
        }

        var resultString = JsonConvert.SerializeObject(result, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }
}
