using System.Text.Json;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Bots.TenantComparison;

internal class CompareLiveTenants : JobWithWaitOctoCommand
{
    private readonly IArgument _sourceTenantIdArg;
    private readonly IArgument _targetTenantIdArg;
    private readonly IArgument _outputFileArg;
    private readonly IArgument _areasArg;
    private readonly IArgument _maxEntitiesPerTypeArg;
    private readonly IArgument _includePropertyDifferencesArg;
    private readonly IArgument _includeAssociationDifferencesArg;

    public CompareLiveTenants(ILogger<CompareLiveTenants> logger, IOptions<OctoToolOptions> options,
        IBotServicesClient botServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.BotServicesGroup, "CompareLiveTenants",
            "Compares two live tenants and generates a comparison report", options, botServicesClient, authenticationService)
    {
        _sourceTenantIdArg = CommandArgumentValue.AddArgument("stid", "sourceTenantId",
            ["Source tenant ID"], true, 1);
        _targetTenantIdArg = CommandArgumentValue.AddArgument("ttid", "targetTenantId",
            ["Target tenant ID"], true, 1);
        _outputFileArg = CommandArgumentValue.AddArgument("o", "output",
            ["Output file path for comparison result"], true, 1);
        
        _areasArg = CommandArgumentValue.AddArgument("a", "areas",
            ["Comparison areas (metadata, models, entities, associations, or All)"], false, 1);
        _maxEntitiesPerTypeArg = CommandArgumentValue.AddArgument("max", "maxEntitiesPerType",
            ["Maximum number of entities to compare per type"], false, 1);
        _includePropertyDifferencesArg = CommandArgumentValue.AddArgument("ipd", "includePropDiff",
            ["Include detailed property differences"], false, 0);
        _includeAssociationDifferencesArg = CommandArgumentValue.AddArgument("iad", "includeAssocDiff",
            ["Include association differences"], false, 0);
    }

    public override async Task Execute()
    {
        var sourceTenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_sourceTenantIdArg).ToLower();
        var targetTenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_targetTenantIdArg).ToLower();
        var outputFile = CommandArgumentValue.GetArgumentScalarValue<string>(_outputFileArg);

        var tenantId = Options.Value.TenantId;
        if (string.IsNullOrEmpty(tenantId))
        {
            throw ToolException.NoTenantIdConfigured();
        }

        Logger.LogInformation(
            "Starting comparison of live tenants '{SourceTenantId}' and '{TargetTenantId}' at '{ServiceClientServiceUri}'",
            sourceTenantId, targetTenantId, ServiceClient.ServiceUri);

        // Build options JSON if any options are provided
        string? optionsJson = null;
        if (CommandArgumentValue.IsArgumentUsed(_areasArg) ||
            CommandArgumentValue.IsArgumentUsed(_maxEntitiesPerTypeArg) ||
            CommandArgumentValue.IsArgumentUsed(_includePropertyDifferencesArg) ||
            CommandArgumentValue.IsArgumentUsed(_includeAssociationDifferencesArg))
        {
            var options = new
            {
                areas = CommandArgumentValue.IsArgumentUsed(_areasArg)
                    ? CommandArgumentValue.GetArgumentScalarValue<string>(_areasArg)
                    : "All",
                maxEntitiesPerType = CommandArgumentValue.IsArgumentUsed(_maxEntitiesPerTypeArg)
                    ? CommandArgumentValue.GetArgumentScalarValue<int?>(_maxEntitiesPerTypeArg)
                    : null,
                includePropertyDifferences = CommandArgumentValue.IsArgumentUsed(_includePropertyDifferencesArg) || true,
                includeAssociationDifferences = CommandArgumentValue.IsArgumentUsed(_includeAssociationDifferencesArg) || true
            };

            optionsJson = JsonSerializer.Serialize(options);
        }

        var response = await ServiceClient.CompareLiveTenantsAsync(tenantId, sourceTenantId, targetTenantId, optionsJson);
        Logger.LogInformation("Tenant comparison job with id '{JobId}' has been started", response.JobId);

        await WaitForJob(response.JobId);

        Logger.LogInformation(
            "Comparison of tenants '{SourceTenantId}' and '{TargetTenantId}' completed",
            sourceTenantId, targetTenantId);

        await DownloadJobResultAsync(tenantId, response.JobId, outputFile);
    }
}
