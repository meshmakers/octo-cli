using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

public class ActivateArchiveCommand : ServiceClientOctoCommand<IStreamDataServicesClient>
{
    private readonly IArgument _archiveRtIdArg;

    public ActivateArchiveCommand(ILogger<ActivateArchiveCommand> logger,
        IOptions<OctoToolOptions> options, IStreamDataServicesClient serviceClient,
        IAuthenticationService authenticationService) : base(logger, Constants.AssetRepositoryServicesGroup,
        "ActivateArchive",
        "Activates a CkArchive: provisions the per-archive CrateDB table and transitions the archive to Activated. Use this once after rt-importing the archive entity.",
        options, serviceClient, authenticationService)
    {
        _archiveRtIdArg = CommandArgumentValue.AddArgument("id", "identifier",
            ["Runtime id of the CkArchive entity to activate"], true, 1);
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
            "Activating archive '{ArchiveRtId}' for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            archiveRtId, Options.Value.TenantId, ServiceClient.ServiceUri);

        await ServiceClient.ActivateArchiveAsync(Options.Value.TenantId, archiveRtId);

        Logger.LogInformation(
            "Archive '{ArchiveRtId}' for tenant '{TenantId}' activated",
            archiveRtId, Options.Value.TenantId);
    }
}
