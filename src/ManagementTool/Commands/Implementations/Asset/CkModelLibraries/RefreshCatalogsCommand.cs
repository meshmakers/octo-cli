using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.CkModelLibraries;

internal class RefreshCatalogsCommand : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IArgument _catalogNameArg;

    public RefreshCatalogsCommand(ILogger<RefreshCatalogsCommand> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "RefreshCatalogs",
            "Refreshes CK model catalog caches. Use -cn to refresh a specific catalog.",
            options, assetServicesClient, authenticationService)
    {
        _catalogNameArg = CommandArgumentValue.AddArgument("cn", "catalogName",
            ["Catalog name to refresh (optional, refreshes all if omitted)"], false, 1);
    }

    public override async Task Execute()
    {
        string? catalogName = CommandArgumentValue.IsArgumentUsed(_catalogNameArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_catalogNameArg)
            : null;

        if (catalogName != null)
            Logger.LogInformation("Refreshing catalog '{CatalogName}'...", catalogName);
        else
            Logger.LogInformation("Refreshing all catalogs...");

        await ServiceClient.RefreshCkModelCatalogsAsync(catalogName);
        Logger.LogInformation("Catalog cache refreshed.");
    }
}
