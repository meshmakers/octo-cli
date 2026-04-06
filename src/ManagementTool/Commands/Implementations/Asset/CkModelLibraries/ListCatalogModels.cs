using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.CkModelLibraries;

internal class ListCatalogModels : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IArgument _catalogNameArg;
    private readonly IArgument _searchArg;

    public ListCatalogModels(ILogger<ListCatalogModels> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "ListCatalogModels",
            "Lists CK models from catalogs. Use -cn to filter by catalog, -q to search.",
            options, assetServicesClient, authenticationService)
    {
        _catalogNameArg = CommandArgumentValue.AddArgument("cn", "catalogName",
            ["Catalog name to filter by"], false, 1);
        _searchArg = CommandArgumentValue.AddArgument("q", "search",
            ["Search term"], false, 1);
    }

    public override async Task Execute()
    {
        string? catalogName = CommandArgumentValue.IsArgumentUsed(_catalogNameArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_catalogNameArg)
            : null;
        string? searchTerm = CommandArgumentValue.IsArgumentUsed(_searchArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_searchArg)
            : null;

        var result = await ServiceClient.ListCkModelCatalogModelsAsync(catalogName, searchTerm);

        Logger.LogInformation("{Count} model(s) found (total: {Total}):", result.Items.Count, result.TotalCount);
        Logger.LogInformation("{Name,-25} {Version,-12} {Catalog}", "NAME", "VERSION", "CATALOG");
        Logger.LogInformation("{S1,-25} {S2,-12} {S3}", new string('-', 25), new string('-', 12), new string('-', 25));

        foreach (var model in result.Items)
        {
            Logger.LogInformation("{Name,-25} {Version,-12} {Catalog}", model.Name, model.Version, model.CatalogName);
        }
    }
}
