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
            "Imports all CK models that need update or fix. Updates that are blocked only by each other (exact pins) " +
            "are imported together in one guarded set. Updates that would break an installed model even then are skipped " +
            "and reported (FixAll never forces them; use ImportFromCatalog --force deliberately). " +
            "Use -w to wait, -y to skip confirmation.",
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

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_waitForJobArg)], description: "Interactive with wait"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_waitForJobArg),
                    new CodeSampleArgument(_yesArg),
                ],
                    description: "Non-interactive (for CI/CD)"),
            ]
        );

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

        // Tenant upgrade guard (AB#6870): a blocked row has HasUpdate but not NeedsAction. Models that pin each other
        // exactly block each other one by one but not as a group (AB#7376): blocked rows with a compatible catalog
        // update join the candidate set and are judged together. What stays blocked in the joint set is dropped below
        // and reported. FixAll never forces.
        var blockedModels = status.Items.Where(i => i.IsBlocked && !i.IsServiceManaged).ToList();
        var jointRows = blockedModels
            .Where(i => i.HasUpdate && i.IsCompatible && i.CatalogName != null && i.FullModelId != null)
            .ToList();
        ReportSkipped(blockedModels.Except(jointRows)
            .Select(m => (m.Name, m.CatalogVersion, (IReadOnlyList<CkUpgradeBlockerDto>)m.Blockers)));
        actionModels = actionModels.Concat(jointRows).ToList();

        if (actionModels.Count == 0)
        {
            Logger.LogInformation(blockedModels.Count == 0
                ? "All models are up to date. Nothing to do."
                : "Nothing to import: the remaining updates are blocked (see above).");
            return;
        }

        Logger.LogInformation("{Count} model(s) need action:", actionModels.Count);
        foreach (var m in actionModels)
            Logger.LogInformation("  {Name}: {Installed} -> {Catalog}", m.Name, m.InstalledVersion ?? "not installed", m.CatalogVersion);

        var requests = actionModels
            .Select(m => new ImportFromCatalogRequestDto { CatalogName = m.CatalogName!, ModelId = m.FullModelId! })
            .ToList();

        // The resolved import set (models plus the dependencies they pull in) is judged together by the service. When it
        // is blocked, drop the requested models whose tree contains a blocking update and resolve the rest again - the
        // unblocked models are still imported.
        var depResult = await _assetServicesClient.ResolveDependenciesBatchAsync(tenantId, requests);
        while (depResult.IsBlocked)
        {
            var dropped = requests
                .Select(r => (Request: r, Blockers: BlockersOfTree(r, depResult)))
                .Where(x => x.Blockers.Count > 0)
                .ToList();
            if (dropped.Count == 0)
            {
                Logger.LogError("The import set would break installed models and no single model can be left out; nothing was imported:");
                UpgradeGuardReport.LogBlockers(Logger, LogLevel.Error, depResult.Blockers);
                throw new ToolException("The resolved import set would break installed models (CK_UPGRADE_BLOCKED); " +
                                        "nothing was imported. FixAll never forces.");
            }

            ReportSkipped(dropped.Select(x => (x.Request.ModelId, (string?)null, (IReadOnlyList<CkUpgradeBlockerDto>)x.Blockers)));
            requests = requests.Except(dropped.Select(x => x.Request)).ToList();
            if (requests.Count == 0)
            {
                Logger.LogInformation("Nothing to import: all remaining updates are blocked (see above).");
                return;
            }

            depResult = await _assetServicesClient.ResolveDependenciesBatchAsync(tenantId, requests);
        }

        if (depResult.ModelsToImport.Count == 0)
        {
            Logger.LogInformation("No models to import after dependency resolution.");
            return;
        }

        // Dependency order (AB#7376): the service imports in the order it receives the ids.
        var modelsToImport = JointImportPlanner.OrderByDependencies(depResult);
        Logger.LogInformation("Models to import ({Count}): {Models}", modelsToImport.Count, string.Join(", ", modelsToImport));

        if (!CommandArgumentValue.IsArgumentUsed(_yesArg))
            _confirmationService.Confirm($"Import {modelsToImport.Count} model(s)");

        var catalogName = requests[0].CatalogName;
        BatchImportResponseDto importResult;
        try
        {
            // FixAll never sends the force flag (ForceBreakingUpdate stays false): an update that breaks an installed
            // model is a deliberate decision of an operator (ImportFromCatalog --force), never a bulk side effect.
            importResult = await _assetServicesClient.ImportFromCatalogBatchAsync(tenantId,
                new ImportFromCatalogBatchRequestDto
                {
                    CatalogName = catalogName, ModelIds = modelsToImport, ForceBreakingUpdate = false
                });
        }
        catch (CkUpgradeBlockedException blocked)
        {
            Logger.LogError("The service refused the import; nothing was imported:");
            UpgradeGuardReport.LogBlockers(Logger, LogLevel.Error, blocked.Blockers);
            throw new ToolException("The import set would break installed models (CK_UPGRADE_BLOCKED); nothing was " +
                                    "imported. FixAll never forces.", blocked);
        }

        Logger.LogInformation("Import with job id \'{Id}\' has been started", importResult.JobId);
        await WaitForJob(importResult.JobId);
    }

    private void ReportSkipped(IEnumerable<(string Name, string? Version, IReadOnlyList<CkUpgradeBlockerDto> Blockers)> skipped)
    {
        var list = skipped.ToList();
        if (list.Count == 0)
        {
            return;
        }

        Logger.LogWarning("{Count} update(s) skipped because they would break installed models (FixAll never forces):", list.Count);
        foreach (var (name, version, blockers) in list)
        {
            Logger.LogWarning("  {Name}{Version}", name, version == null ? string.Empty : $" -> {version}");
            UpgradeGuardReport.LogBlockers(Logger, LogLevel.Warning, blockers, "    ");
        }
    }

    /// <summary>
    ///     The blockers of the combined import set that are caused by a model inside the dependency tree of
    ///     <paramref name="request" /> (the request itself or a dependency it pulls in).
    /// </summary>
    private static List<CkUpgradeBlockerDto> BlockersOfTree(ImportFromCatalogRequestDto request,
        BatchDependencyResolutionResponseDto resolution)
    {
        var tree = resolution.DependencyTrees.FirstOrDefault(t => t.RootModel?.ModelId == request.ModelId);
        if (tree?.RootModel == null)
        {
            return [];
        }

        return resolution.Blockers.Where(b => TreeContains(tree.RootModel, b.Update)).ToList();
    }

    private static bool TreeContains(DependencyResolutionItemDto item, string modelId) =>
        item.ModelId == modelId || item.Dependencies.Any(d => TreeContains(d, modelId));
}
