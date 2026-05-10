using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

public class RetryArchiveActivationCommand : ServiceClientOctoCommand<IStreamDataServicesClient>
{
    private readonly IArgument _archiveRtIdArg;

    public RetryArchiveActivationCommand(ILogger<RetryArchiveActivationCommand> logger,
        IOptions<OctoToolOptions> options, IStreamDataServicesClient serviceClient,
        IAuthenticationService authenticationService) : base(logger, Constants.AssetRepositoryServicesGroup,
        "RetryArchiveActivation",
        "Retries activation after a previous DDL failure. Allowed only from Failed.",
        options, serviceClient, authenticationService)
    {
        _archiveRtIdArg = CommandArgumentValue.AddArgument("id", "identifier",
            ["Runtime id of the CkArchive entity to retry activation for"], true, 1);
    }

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        var archiveRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_archiveRtIdArg);

        Logger.LogInformation(
            "Retrying activation of archive '{ArchiveRtId}' for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            archiveRtId, Options.Value.TenantId, ServiceClient.ServiceUri);

        await ServiceClient.RetryArchiveActivationAsync(Options.Value.TenantId, archiveRtId);

        Logger.LogInformation("Archive '{ArchiveRtId}' for tenant '{TenantId}' activation retried",
            archiveRtId, Options.Value.TenantId);
    }
}
