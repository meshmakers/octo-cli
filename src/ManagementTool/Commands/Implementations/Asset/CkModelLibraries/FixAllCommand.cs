using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.CkModelCatalog;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.CkModelLibraries;

internal class FixAllCommand : JobWithWaitOctoCommand
{
    private readonly IAssetServicesClient _assetServicesClient;
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _yesArg;

    public FixAllCommand(ILogger<FixAllCommand> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IBotServicesClient botServicesClient,
        IAuthenticationService authenticationService, IConfirmationService confirmationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "FixAll",
            "Imports all CK models that need update or fix. Use -w to wait, -y to skip confirmation.",
            options, botServicesClient, authenticationService)
    {
        _assetServicesClient = assetServicesClient;
        _confirmationService = confirmationService;
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
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

        Logger.LogInformation("Checking library status...");
        var status = await _assetServicesClient.GetLibraryStatusAsync(tenantId);
        var actionModels = status.Items
            .Where(i => i.NeedsAction && !i.IsServiceManaged && i.IsCompatible && i.CatalogName != null && i.FullModelId != null)
            .ToList();

        if (actionModels.Count == 0)
        {
            Logger.LogInformation("All models are up to date. Nothing to do.");
            return;
        }

        Logger.LogInformation("{Count} model(s) need action:", actionModels.Count);
        foreach (var m in actionModels)
            Logger.LogInformation("  {Name}: {Installed} -> {Catalog}", m.Name, m.InstalledVersion ?? "not installed", m.CatalogVersion);

        var requests = actionModels
            .Select(m => new ImportFromCatalogRequestDto { CatalogName = m.CatalogName!, ModelId = m.FullModelId! })
            .ToList();

        var depResult = await _assetServicesClient.ResolveDependenciesBatchAsync(tenantId, requests);

        if (depResult.ModelsToImport.Count == 0)
        {
            Logger.LogInformation("No models to import after dependency resolution.");
            return;
        }

        Logger.LogInformation("Models to import ({Count}): {Models}", depResult.ModelsToImport.Count, string.Join(", ", depResult.ModelsToImport));

        if (!CommandArgumentValue.IsArgumentUsed(_yesArg))
            _confirmationService.Confirm($"Import {depResult.ModelsToImport.Count} model(s)");

        var catalogName = requests[0].CatalogName;
        var importResult = await _assetServicesClient.ImportFromCatalogBatchAsync(tenantId,
            new ImportFromCatalogBatchRequestDto { CatalogName = catalogName, ModelIds = depResult.ModelsToImport });

        for (var i = 0; i < importResult.JobIds.Count; i++)
        {
            var jobModelName = i < depResult.ModelsToImport.Count ? depResult.ModelsToImport[i] : "Model";
            Logger.LogInformation("Importing {Model} ({Current}/{Total})...", jobModelName, i + 1, importResult.JobIds.Count);
            await WaitForJob(importResult.JobIds[i]);
        }

        Logger.LogInformation("Fix All completed.");
    }
}
