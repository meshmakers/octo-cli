using System.Text.Json;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Bots.TenantComparison;

internal class CompareLiveTenantWithBackup : JobWithWaitOctoCommand
{
    private readonly IArgument _tenantIdArg;
    private readonly IArgument _backupFileArg;
    private readonly IArgument _outputFileArg;
    private readonly IArgument _areasArg;
    private readonly IArgument _maxEntitiesPerTypeArg;
    private readonly IArgument _includePropertyDifferencesArg;
    private readonly IArgument _includeAssociationDifferencesArg;
    private readonly IArgument _viewArg;

    public CompareLiveTenantWithBackup(ILogger<CompareLiveTenantWithBackup> logger, IOptions<OctoToolOptions> options,
        IBotServicesClient botServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.BotServicesGroup, "CompareLiveTenantWithBackup",
            "Compares a live tenant with a backup archive", options, botServicesClient, authenticationService)
    {
        _tenantIdArg = CommandArgumentValue.AddArgument("stid", "sourceTenantId",
            ["Live tenant ID"], true, 1);
        _backupFileArg = CommandArgumentValue.AddArgument("tf", "targetBackupFile",
            ["Backup file path (*.tar.gz)"], true, 1);
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
        _viewArg = CommandArgumentValue.AddArgument("v", "view",
            ["Open viewer in browser after completion"], false, 0);
    }

    public override async Task Execute()
    {
        var sourceTenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg).ToLower();
        var backupFile = CommandArgumentValue.GetArgumentScalarValue<string>(_backupFileArg);
        var outputFile = CommandArgumentValue.GetArgumentScalarValue<string>(_outputFileArg);

        var tenantId = Options.Value.TenantId;
        if (string.IsNullOrEmpty(tenantId))
        {
            throw ToolException.NoTenantIdConfigured();
        }

        Logger.LogInformation(
            "Starting comparison of live tenant '{TenantId}' with backup '{BackupFile}' at '{ServiceClientServiceUri}'",
            sourceTenantId, backupFile, ServiceClient.ServiceUri);

        if (!File.Exists(backupFile))
        {
            throw ToolException.FilePathDoesNotExist(backupFile);
        }

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
                includePropertyDifferences =
                    CommandArgumentValue.IsArgumentUsed(_includePropertyDifferencesArg) || true,
                includeAssociationDifferences =
                    CommandArgumentValue.IsArgumentUsed(_includeAssociationDifferencesArg) || true
            };

            optionsJson = JsonSerializer.Serialize(options);
        }

        var response =
            await ServiceClient.CompareLiveTenantWithBackupAsync(sourceTenantId, backupFile, optionsJson);
        Logger.LogInformation("Tenant comparison job with id '{JobId}' has been started", response.JobId);

        await WaitForJob(response.JobId);

        Logger.LogInformation(
            "Comparison of tenant '{TenantId}' with backup completed", sourceTenantId);

        await DownloadJobResultAsync(tenantId, response.JobId, outputFile);

        // Open viewer if requested
        if (CommandArgumentValue.IsArgumentUsed(_viewArg))
        {
            ComparisonViewerGenerator.GenerateAndOpen(outputFile, Logger);
        }
    }
}