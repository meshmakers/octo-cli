using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

public class DeleteArchiveCommand : ServiceClientOctoCommand<IStreamDataServicesClient>
{
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _archiveRtIdArg;
    private readonly IArgument _yesArg;

    public DeleteArchiveCommand(ILogger<DeleteArchiveCommand> logger,
        IOptions<OctoToolOptions> options, IStreamDataServicesClient serviceClient,
        IAuthenticationService authenticationService,
        IConfirmationService confirmationService) : base(logger, Constants.AssetRepositoryServicesGroup,
        "DeleteArchive",
        "Drops the per-archive CrateDB table and soft-deletes the CkArchive entity. Destructive — historical data is lost.",
        options, serviceClient, authenticationService)
    {
        _confirmationService = confirmationService;
        _archiveRtIdArg = CommandArgumentValue.AddArgument("id", "identifier",
            ["Runtime id of the CkArchive entity to delete"], true, 1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes",
            ["Skip confirmation prompt"], false, 0);
    }

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        var archiveRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_archiveRtIdArg);

        if (!CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm(
                $"Are you sure you want to delete archive '{archiveRtId}'? The CrateDB table will be dropped and historical data lost."))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation(
            "Deleting archive '{ArchiveRtId}' for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            archiveRtId, Options.Value.TenantId, ServiceClient.ServiceUri);

        await ServiceClient.DeleteArchiveAsync(Options.Value.TenantId, archiveRtId);

        Logger.LogInformation("Archive '{ArchiveRtId}' for tenant '{TenantId}' deleted",
            archiveRtId, Options.Value.TenantId);
    }
}
