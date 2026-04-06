using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.CkModelCatalog;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.CkModelLibraries;

internal class ImportFromCatalogCommand : JobWithWaitOctoCommand
{
    private readonly IAssetServicesClient _assetServicesClient;
    private readonly IArgument _catalogNameArg;
    private readonly IArgument _modelIdArg;

    public ImportFromCatalogCommand(ILogger<ImportFromCatalogCommand> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IBotServicesClient botServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "ImportFromCatalog",
            "Imports a CK model from a catalog with all dependencies. Use -w to wait for completion.",
            options, botServicesClient, authenticationService)
    {
        _assetServicesClient = assetServicesClient;
        _catalogNameArg = CommandArgumentValue.AddArgument("cn", "catalogName", ["Catalog name"], true, 1);
        _modelIdArg = CommandArgumentValue.AddArgument("m", "modelId", ["Model ID (e.g., Industry.Energy-2.0.0)"], true, 1);
    }

    public override async Task PreValidate()
    {
        await base.PreValidate();
        _assetServicesClient.AccessToken.AccessToken = ServiceClient.AccessToken.AccessToken;
    }

    public override async Task Execute()
    {
        var tenantId = Options.Value.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
            throw ToolException.NoTenantIdConfigured();

        var catalogName = CommandArgumentValue.GetArgumentScalarValue<string>(_catalogNameArg);
        var modelId = CommandArgumentValue.GetArgumentScalarValue<string>(_modelIdArg);

        Logger.LogInformation("Resolving dependencies for {ModelId}...", modelId);
        var depResult = await _assetServicesClient.ResolveDependenciesBatchAsync(tenantId,
            [new ImportFromCatalogRequestDto { CatalogName = catalogName, ModelId = modelId }]);

        if (depResult.ModelsToImport.Count == 0)
        {
            Logger.LogInformation("No models need to be imported.");
            return;
        }

        Logger.LogInformation("Models to import: {Models}", string.Join(", ", depResult.ModelsToImport));

        var importResult = await _assetServicesClient.ImportFromCatalogBatchAsync(tenantId,
            new ImportFromCatalogBatchRequestDto { CatalogName = catalogName, ModelIds = depResult.ModelsToImport });

        for (var i = 0; i < importResult.JobIds.Count; i++)
        {
            var jobModelName = i < depResult.ModelsToImport.Count ? depResult.ModelsToImport[i] : "Model";
            Logger.LogInformation("Importing {Model} ({Current}/{Total})...", jobModelName, i + 1, importResult.JobIds.Count);
            await WaitForJob(importResult.JobIds[i]);
        }

        Logger.LogInformation("Import completed.");
    }
}
