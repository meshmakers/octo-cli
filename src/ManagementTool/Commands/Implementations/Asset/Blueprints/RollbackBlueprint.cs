using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Blueprints;

internal class RollbackBlueprint : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _backupIdArg;
    private readonly IArgument _yesArg;

    public RollbackBlueprint(
        ILogger<RollbackBlueprint> logger,
        IConsoleService consoleService,
        IConfirmationService confirmationService,
        IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "RollbackBlueprint",
            "Rolls the active tenant back to a previously-created blueprint backup.",
            options, assetServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _confirmationService = confirmationService;

        _backupIdArg = CommandArgumentValue.AddArgument("bid", "backupId",
            ["Identifier of the backup to restore from"], true, 1);

        _yesArg = CommandArgumentValue.AddArgument("y", "yes",
            ["Skip the interactive confirmation prompt"], false, 0);
    }

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing - configure it via the active context");
            return;
        }

        var backupId = CommandArgumentValue.GetArgumentScalarValue<string>(_backupIdArg);
        var skipConfirmation = CommandArgumentValue.IsArgumentUsed(_yesArg);

        if (!skipConfirmation && !_confirmationService.Confirm(
                $"rollback tenant '{Options.Value.TenantId}' to backup '{backupId}'? Current tenant data will be replaced"))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation(
            "Rolling back tenant '{TenantId}' to backup '{BackupId}' at '{ServiceClientServiceUri}'",
            Options.Value.TenantId, backupId, ServiceClient.ServiceUri);

        var result = await ServiceClient.RestoreBlueprintBackupAsync(Options.Value.TenantId, backupId);

        if (!result.Success)
        {
            Logger.LogError("Rollback failed: {Messages}", string.Join("; ", result.Messages));
            return;
        }

        Logger.LogInformation(
            "Rollback complete: {EntitiesRestored} entities restored",
            result.EntitiesRestored);

        var resultString = JsonConvert.SerializeObject(result, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }
}
