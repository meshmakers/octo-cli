using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

public class EnableArchiveCommand : ServiceClientOctoCommand<IStreamDataServicesClient>
{
    private readonly IArgument _archiveRtIdArg;

    public EnableArchiveCommand(ILogger<EnableArchiveCommand> logger,
        IOptions<OctoToolOptions> options, IStreamDataServicesClient serviceClient,
        IAuthenticationService authenticationService) : base(logger, Constants.AssetRepositoryServicesGroup,
        "EnableArchive",
        "Re-enables a previously disabled archive: transitions Disabled → Activated. Re-validates column paths against the current CK model; no DDL.",
        options, serviceClient, authenticationService)
    {
        _archiveRtIdArg = CommandArgumentValue.AddArgument("id", "identifier",
            ["Runtime id of the CkArchive entity to re-enable"], true, 1);
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
            "Enabling archive '{ArchiveRtId}' for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            archiveRtId, Options.Value.TenantId, ServiceClient.ServiceUri);

        await ServiceClient.EnableArchiveAsync(Options.Value.TenantId, archiveRtId);

        Logger.LogInformation("Archive '{ArchiveRtId}' for tenant '{TenantId}' enabled",
            archiveRtId, Options.Value.TenantId);
    }
}
