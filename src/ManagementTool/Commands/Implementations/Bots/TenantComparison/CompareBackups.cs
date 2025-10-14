using System.Text.Json;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Bots.TenantComparison;

internal class CompareBackups : JobWithWaitOctoCommand
{
    private readonly IArgument _sourceBackupFileArg;
    private readonly IArgument _targetBackupFileArg;
    private readonly IArgument _outputFileArg;
    private readonly IArgument _areasArg;
    private readonly IArgument _maxEntitiesPerTypeArg;
    private readonly IArgument _includePropertyDifferencesArg;
    private readonly IArgument _includeAssociationDifferencesArg;

    public CompareBackups(ILogger<CompareBackups> logger, IOptions<OctoToolOptions> options,
        IBotServicesClient botServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.BotServicesGroup, "CompareBackups",
            "Compares two backup archives", options, botServicesClient, authenticationService)
    {
        _sourceBackupFileArg = CommandArgumentValue.AddArgument("sf", "sourceFile",
            ["Source backup file path (*.tar.gz)"], true, 1);
        _targetBackupFileArg = CommandArgumentValue.AddArgument("tf", "targetFile",
            ["Target backup file path (*.tar.gz)"], true, 1);
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
        var sourceBackupFile = CommandArgumentValue.GetArgumentScalarValue<string>(_sourceBackupFileArg);
        var targetBackupFile = CommandArgumentValue.GetArgumentScalarValue<string>(_targetBackupFileArg);
        var outputFile = CommandArgumentValue.GetArgumentScalarValue<string>(_outputFileArg);

        var tenantId = Options.Value.TenantId;
        if (string.IsNullOrEmpty(tenantId))
        {
            throw ToolException.NoTenantIdConfigured();
        }

        Logger.LogInformation(
            "Starting comparison of backup '{SourceBackupFile}' with '{TargetBackupFile}' at '{ServiceClientServiceUri}'",
            sourceBackupFile, targetBackupFile, ServiceClient.ServiceUri);

        if (!File.Exists(sourceBackupFile))
        {
            throw ToolException.FilePathDoesNotExist(sourceBackupFile);
        }

        if (!File.Exists(targetBackupFile))
        {
            throw ToolException.FilePathDoesNotExist(targetBackupFile);
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
                includePropertyDifferences = CommandArgumentValue.IsArgumentUsed(_includePropertyDifferencesArg) || true,
                includeAssociationDifferences = CommandArgumentValue.IsArgumentUsed(_includeAssociationDifferencesArg) || true
            };

            optionsJson = JsonSerializer.Serialize(options);
        }

        var response = await ServiceClient.CompareBackupsAsync(sourceBackupFile, targetBackupFile, optionsJson);
        Logger.LogInformation("Backup comparison job with id '{JobId}' has been started", response.JobId);

        await WaitForJob(response.JobId);

        Logger.LogInformation("Comparison of backups completed");

        await DownloadJobResultAsync(tenantId, response.JobId, outputFile);
    }
}
