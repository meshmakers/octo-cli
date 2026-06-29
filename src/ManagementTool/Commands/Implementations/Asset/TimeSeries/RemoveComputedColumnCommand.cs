using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

public class RemoveComputedColumnCommand : ServiceClientOctoCommand<IStreamDataServicesClient>
{
    private readonly IArgument _archiveRtIdArg;
    private readonly IArgument _nameArg;

    public RemoveComputedColumnCommand(ILogger<RemoveComputedColumnCommand> logger,
        IOptions<OctoToolOptions> options, IStreamDataServicesClient serviceClient,
        IAuthenticationService authenticationService) : base(logger, Constants.AssetRepositoryServicesGroup,
        "RemoveComputedColumn",
        "Removes a computed column from an archive. Rejected when another computed column still references it. (AB#4189)",
        options, serviceClient, authenticationService)
    {
        _archiveRtIdArg = CommandArgumentValue.AddArgument("id", "identifier",
            ["Runtime id of the archive to remove the computed column from"], true, 1);
        _nameArg = CommandArgumentValue.AddArgument("n", "name",
            ["Name of the computed column to remove"], true, 1);
    }

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        var archiveRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_archiveRtIdArg);
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg);

        Logger.LogInformation("Removing computed column '{Name}' from archive '{ArchiveRtId}' (tenant '{TenantId}')",
            name, archiveRtId, Options.Value.TenantId);

        await ServiceClient.RemoveComputedColumnAsync(Options.Value.TenantId, archiveRtId, name);

        Logger.LogInformation("Computed column '{Name}' removed from archive '{ArchiveRtId}'.", name, archiveRtId);
    }
}
