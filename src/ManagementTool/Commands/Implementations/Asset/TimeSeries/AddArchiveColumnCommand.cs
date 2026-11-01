using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

/// <summary>
///     AB#7997: adds an ingested column (an existing attribute of the target CK type) to an Activated archive without
///     recreating it. Existing rows keep their history and read null for the new column; the next ingested rows fill it.
/// </summary>
public class AddArchiveColumnCommand : ServiceClientOctoCommand<IStreamDataServicesClient>
{
    private readonly IArgument _archiveRtIdArg;
    private readonly IArgument _pathArg;
    private readonly IArgument _indexedArg;

    public AddArchiveColumnCommand(ILogger<AddArchiveColumnCommand> logger,
        IOptions<OctoToolOptions> options, IStreamDataServicesClient serviceClient,
        IAuthenticationService authenticationService) : base(logger, Constants.AssetRepositoryServicesGroup,
        "AddArchiveColumn",
        "Adds a column for an existing attribute of the target CK type to an Activated raw or time-range archive without recreating it. The table gains a nullable column (existing rows keep their data and read null), then the archive definition; the next ingested rows fill it. Idempotent. A computed column is added with AddComputedColumn. (AB#7997)",
        options, serviceClient, authenticationService)
    {
        _archiveRtIdArg = CommandArgumentValue.AddArgument("id", "identifier",
            ["Runtime id of the archive to add the column to"], true, 1);
        _pathArg = CommandArgumentValue.AddArgument("p", "path",
            ["Attribute path on the archive's target CK type, e.g. 'DischargingPower' or 'Amount.Value'"], true, 1);
        _indexedArg = CommandArgumentValue.AddArgument("x", "indexed",
            ["Whether to index the physical column ('true'/'false'). Defaults to false"], false, 1);
    }

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        var archiveRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_archiveRtIdArg);
        var path = CommandArgumentValue.GetArgumentScalarValue<string>(_pathArg);
        var indexed = CommandArgumentValue.IsArgumentUsed(_indexedArg)
            && string.Equals(CommandArgumentValue.GetArgumentScalarValue<string>(_indexedArg), "true",
                StringComparison.OrdinalIgnoreCase);

        Logger.LogInformation("Adding column '{Path}' to archive '{ArchiveRtId}' (tenant '{TenantId}')",
            path, archiveRtId, Options.Value.TenantId);

        await ServiceClient.AddArchiveColumnAsync(Options.Value.TenantId, archiveRtId, path, indexed);

        Logger.LogInformation(
            "Archive '{ArchiveRtId}' has column '{Path}'; existing rows read null for it, new rows fill it.",
            archiveRtId, path);
    }
}
