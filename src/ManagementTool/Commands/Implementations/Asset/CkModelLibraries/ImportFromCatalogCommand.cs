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
    private readonly IArgument _forceArg;
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
        _forceArg = CommandArgumentValue.AddArgument("f", "force",
            ["Import even if the update would break installed models (tenant upgrade guard). The broken models " +
             "become ResolveFailed until they are updated; without this option the import is refused and nothing is changed"],
            false, 0);
    }

    public override async Task PreValidate()
    {
        await base.PreValidate();
        _assetServicesClient.AccessToken.AccessToken = ServiceClient.AccessToken.AccessToken;
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_catalogNameArg, "PublicGitHubCatalog"),
                    new CodeSampleArgument(_modelIdArg, "Industry.Energy-2.0.0"),
                    new CodeSampleArgument(_waitForJobArg),
                ],
                    description: "Import with wait"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_catalogNameArg, "PublicGitHubCatalog"),
                    new CodeSampleArgument(_modelIdArg, "Industry.Energy-2.0.0"),
                ],
                    description: "Import without waiting (prints the job id)"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_catalogNameArg, "PublicGitHubCatalog"),
                    new CodeSampleArgument(_modelIdArg, "Industry.Energy-2.0.0"),
                    new CodeSampleArgument(_forceArg),
                ],
                    description: "Import although the update breaks an installed model (deliberate override)"),
            ]
        );

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

        var force = CommandArgumentValue.IsArgumentUsed(_forceArg);
        if (depResult.IsBlocked)
        {
            if (!force)
            {
                Logger.LogError("The import would break installed models; nothing was imported:");
                UpgradeGuardReport.LogBlockers(Logger, LogLevel.Error, depResult.Blockers);
                throw new ToolException(
                    $"The import of {modelId} would break {depResult.Blockers.Select(b => b.Dependent).Distinct().Count()} " +
                    "installed model(s) (CK_UPGRADE_BLOCKED). Update the dependent models first, or repeat the command " +
                    "with --force to import anyway.");
            }

            Logger.LogWarning("--force: importing although the update breaks installed models (they become ResolveFailed):");
            UpgradeGuardReport.LogBlockers(Logger, LogLevel.Warning, depResult.Blockers);
        }

        BatchImportResponseDto importResult;
        try
        {
            importResult = await _assetServicesClient.ImportFromCatalogBatchAsync(tenantId,
                new ImportFromCatalogBatchRequestDto
                {
                    CatalogName = catalogName, ModelIds = depResult.ModelsToImport, ForceBreakingUpdate = force
                });
        }
        catch (CkUpgradeBlockedException blocked)
        {
            // The service refused (an older service that did not preview the blockers, or the tenant changed meanwhile).
            Logger.LogError("The service refused the import; nothing was imported:");
            UpgradeGuardReport.LogBlockers(Logger, LogLevel.Error, blocked.Blockers);
            throw new ToolException(
                $"The import of {modelId} would break installed model(s) (CK_UPGRADE_BLOCKED). Update the dependent " +
                "models first, or repeat the command with --force to import anyway.", blocked);
        }

        Logger.LogInformation("Import with job id \'{Id}\' has been started", importResult.JobId);
        await WaitForJob(importResult.JobId);
    }
}
