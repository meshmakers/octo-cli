using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.CkModelLibraries;

internal class ListCatalogs : ServiceClientOctoCommand<IAssetServicesClient>
{
    public ListCatalogs(ILogger<ListCatalogs> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "ListCatalogs",
            "Lists available CK model catalog sources.",
            options, assetServicesClient, authenticationService)
    {
    }

    public override async Task Execute()
    {
        var catalogs = await ServiceClient.GetCkModelCatalogsAsync();

        Logger.LogInformation("{Count} catalog(s) found:", catalogs.Count);
        Logger.LogInformation("{Name,-30} {Description}", "NAME", "DESCRIPTION");
        Logger.LogInformation("{Separator,-30} {Separator}", new string('-', 30), new string('-', 40));

        foreach (var catalog in catalogs)
        {
            Logger.LogInformation("{Name,-30} {Description}", catalog.Name, catalog.Description);
        }
    }
}
