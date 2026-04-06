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
            "Pre-flight check for CK model upgrade/migration.",
            options, assetServicesClient, authenticationService)
    {
        _catalogNameArg = CommandArgumentValue.AddArgument("cn", "catalogName", ["Catalog name"], true, 1);
        _modelIdArg = CommandArgumentValue.AddArgument("m", "modelId", ["Model ID (e.g., Industry.Energy-2.0.0)"], true, 1);
    }

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

        if (result.ErrorMessage != null)
            Logger.LogWarning("Error: {Error}", result.ErrorMessage);
    }
}
