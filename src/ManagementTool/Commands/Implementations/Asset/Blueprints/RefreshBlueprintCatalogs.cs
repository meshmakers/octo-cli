using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Blueprints;

internal class RefreshBlueprintCatalogs : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _catalogNameArg;

    public RefreshBlueprintCatalogs(
        ILogger<RefreshBlueprintCatalogs> logger,
        IConsoleService consoleService,
        IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "RefreshBlueprintCatalogs",
            "Refreshes blueprint catalog caches at the asset repository. Use -cn to refresh a specific catalog.",
            options, assetServicesClient, authenticationService)
    {
        _consoleService = consoleService;

        _catalogNameArg = CommandArgumentValue.AddArgument("cn", "catalogName",
            ["Catalog name to refresh (optional, refreshes all if omitted)"], false, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [], description: "Refresh all blueprint catalogs"),
                new CodeSample(
                    arguments: [new CodeSampleArgument(_catalogNameArg, "LocalFileSystemBlueprintCatalog")],
                    description: "Refresh a specific blueprint catalog"),
            ],
            Notes:
            [
                "The refresh is always forced - freshly published blueprints become visible without a service restart.",
                "A catalog that fails to refresh is reported as Failed but does not abort the refresh of the other catalogs; the command exits with a non-zero code when any catalog failed.",
            ]);

    public override async Task Execute()
    {
        // Normalize with the same IsNullOrWhiteSpace convention the SDK routes by, so a blank
        // -cn value is announced as (and behaves like) a refresh of all catalogs.
        string? catalogName = CommandArgumentValue.IsArgumentUsed(_catalogNameArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_catalogNameArg)
            : null;
        if (string.IsNullOrWhiteSpace(catalogName))
        {
            catalogName = null;
        }

        if (catalogName != null)
        {
            Logger.LogInformation("Refreshing blueprint catalog '{CatalogName}'...", catalogName);
        }
        else
        {
            Logger.LogInformation("Refreshing all blueprint catalogs...");
        }

        var result = await ServiceClient.RefreshBlueprintCatalogsAsync(catalogName);

        foreach (var item in result.Results)
        {
            if (item.Status == "Failed")
            {
                Logger.LogError("Catalog '{CatalogName}': {Status} - {Message}",
                    item.CatalogName, item.Status, item.Message);
            }
            else
            {
                Logger.LogInformation("Catalog '{CatalogName}': {Status}{MessageSuffix}",
                    item.CatalogName, item.Status,
                    string.IsNullOrEmpty(item.Message) ? string.Empty : $" - {item.Message}");
            }
        }

        var refreshedCount = result.Results.Count(r => r.Status == "Refreshed");
        var skippedCount = result.Results.Count(r => r.Status == "Skipped");
        var failedCount = result.Results.Count(r => r.Status == "Failed");
        Logger.LogInformation("Blueprint catalog refresh finished: {RefreshedCount} refreshed, {SkippedCount} skipped, {FailedCount} failed",
            refreshedCount, skippedCount, failedCount);

        var resultString = JsonConvert.SerializeObject(result, Formatting.Indented);
        _consoleService.WriteLine(resultString);

        if (failedCount > 0)
        {
            throw new ToolException($"{failedCount} of {result.Results.Count} catalog(s) failed to refresh - see log above");
        }
    }
}
