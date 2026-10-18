using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.CkModelCatalog;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.CkModelLibraries;

internal class CheckUpgradeCommand : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IArgument _catalogNameArg;
    private readonly IArgument _modelIdArg;

    public CheckUpgradeCommand(ILogger<CheckUpgradeCommand> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "CheckUpgrade",
            "Pre-flight check for CK model upgrade/migration. 'Breaking Changes' is the migration view of the model itself; " +
            "'Would break' (isBlocked/blockers) is the tenant upgrade guard: which installed models the import would break, " +
            "also for System.",
            options, assetServicesClient, authenticationService)
    {
        _catalogNameArg = CommandArgumentValue.AddArgument("cn", "catalogName", ["Catalog name"], true, 1);
        _modelIdArg = CommandArgumentValue.AddArgument("m", "modelId", ["Model ID (e.g., Industry.Energy-2.0.0)"], true, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_catalogNameArg, "PublicGitHubCatalog"),
                    new CodeSampleArgument(_modelIdArg, "Basic-2.0.2"),
                ],
                    description: "Basic usage",
                    expectedOutput: """
                    Model:              Basic
                    Installed Version:  2.0.0
                    Target Version:     2.0.2
                    Upgrade Needed:     True
                    Migration Path:     True
                    Breaking Changes:   False
                    """),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_catalogNameArg, "PublicGitHubCatalog"),
                    new CodeSampleArgument(_modelIdArg, "Basic-2.5.0"),
                ],
                    description: "Blocked update (AB#6869): importing it would break installed models. Read-only, the exit code is unchanged",
                    expectedOutput: """
                    Model:              Basic
                    Installed Version:  2.4.0
                    Target Version:     2.5.0
                    Upgrade Needed:     True
                    Migration Path:     True
                    Breaking Changes:   False
                    Would break:
                      - EnergyCommunity.Registration-2.8.0: exact pin Basic-2.4.0: Basic-2.5.0 replaces it
                          resolve: update EnergyCommunity.Registration to 2.10.0 first
                    """),
            ]
        );

    public override async Task Execute()
    {
        var tenantId = Options.Value.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
            throw ToolException.NoTenantIdConfigured();

        var catalogName = CommandArgumentValue.GetArgumentScalarValue<string>(_catalogNameArg);
        var modelId = CommandArgumentValue.GetArgumentScalarValue<string>(_modelIdArg);

        var result = await ServiceClient.CheckUpgradeAsync(tenantId,
            new ImportFromCatalogRequestDto { CatalogName = catalogName, ModelId = modelId });

        Logger.LogInformation("Model:              {Name}", result.ModelName);
        Logger.LogInformation("Installed Version:  {Version}", result.InstalledVersion ?? "not installed");
        Logger.LogInformation("Target Version:     {Version}", result.TargetVersion);
        Logger.LogInformation("Upgrade Needed:     {Needed}", result.UpgradeNeeded);
        Logger.LogInformation("Migration Path:     {Available}", result.MigrationPathAvailable);
        Logger.LogInformation("Breaking Changes:   {Breaking}", result.HasBreakingChanges);

        // AB#6869: tenant upgrade guard result (AB#6868). Unchanged output for an unblocked update or an older server.
        if (result.IsBlocked)
        {
            Logger.LogInformation("Would break:");
            foreach (var line in UpgradeBlockerFormatter.DetailLines(result.Blockers))
                Logger.LogInformation("{Line}", line);
        }

        if (result.ErrorMessage != null)
            Logger.LogWarning("Error: {Error}", result.ErrorMessage);
    }
}
