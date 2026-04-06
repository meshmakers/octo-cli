using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.CkModelCatalog;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.CkModelLibraries;

internal class CheckDependenciesCommand : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IArgument _catalogNameArg;
    private readonly IArgument _modelIdArg;

    public CheckDependenciesCommand(ILogger<CheckDependenciesCommand> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "CheckDependencies",
            "Shows the dependency tree for a CK model from a catalog.",
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

        var result = await ServiceClient.ResolveDependenciesBatchAsync(tenantId,
            [new ImportFromCatalogRequestDto { CatalogName = catalogName, ModelId = modelId }]);

        foreach (var tree in result.DependencyTrees)
            PrintTree(tree.RootModel, 0);

        if (result.ModelsToImport.Count > 0)
        {
            Logger.LogInformation("");
            Logger.LogInformation("Models to import ({Count}):", result.ModelsToImport.Count);
            foreach (var m in result.ModelsToImport)
                Logger.LogInformation("  {ModelId}", m);
        }
        else
        {
            Logger.LogInformation("No models need to be imported.");
        }
    }

    private void PrintTree(DependencyResolutionItemDto item, int indent)
    {
        var prefix = new string(' ', indent * 2);
        var installed = item.InstalledVersion != null ? $" ({item.InstalledVersion})" : "";
        Logger.LogInformation("{Prefix}{Name} v{Version}  [{Action}]{Installed}",
            prefix, item.Name, item.RequiredVersion, item.Action.ToUpper(), installed);
        foreach (var dep in item.Dependencies)
            PrintTree(dep, indent + 1);
    }
}
