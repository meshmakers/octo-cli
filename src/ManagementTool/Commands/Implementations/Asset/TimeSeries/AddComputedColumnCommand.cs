using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

public class AddComputedColumnCommand : ServiceClientOctoCommand<IStreamDataServicesClient>
{
    private readonly IArgument _archiveRtIdArg;
    private readonly IArgument _nameArg;
    private readonly IArgument _formulaArg;
    private readonly IArgument _resultTypeArg;
    private readonly IArgument _indexedArg;

    public AddComputedColumnCommand(ILogger<AddComputedColumnCommand> logger,
        IOptions<OctoToolOptions> options, IStreamDataServicesClient serviceClient,
        IAuthenticationService authenticationService) : base(logger, Constants.AssetRepositoryServicesGroup,
        "AddComputedColumn",
        "Adds a computed column to an Activated raw or time-range archive and backfills it. The column becomes visible atomically when the backfill completes. (AB#4189)",
        options, serviceClient, authenticationService)
    {
        _archiveRtIdArg = CommandArgumentValue.AddArgument("id", "identifier",
            ["Runtime id of the archive to add the computed column to"], true, 1);
        _nameArg = CommandArgumentValue.AddArgument("n", "name",
            ["Output column name — the identifier formulas reference and the column appears under"], true, 1);
        _formulaArg = CommandArgumentValue.AddArgument("f", "formula",
            ["mXparser formula over other columns of the same row (e.g. 'activePower / apparentPower')"], true, 1);
        _resultTypeArg = CommandArgumentValue.AddArgument("r", "resultType",
            ["Declared result type the formula is cast back to: Boolean | Int | Int64 | Double | DateTime"], true, 1);
        _indexedArg = CommandArgumentValue.AddArgument("x", "indexed",
            ["Whether to index the physical column ('true'/'false'). Defaults to true"], false, 1);
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
        var formula = CommandArgumentValue.GetArgumentScalarValue<string>(_formulaArg);
        var resultType = CommandArgumentValue.GetArgumentScalarValue<string>(_resultTypeArg);
        var indexed = !CommandArgumentValue.IsArgumentUsed(_indexedArg)
            || !string.Equals(CommandArgumentValue.GetArgumentScalarValue<string>(_indexedArg), "false",
                StringComparison.OrdinalIgnoreCase);

        Logger.LogInformation(
            "Adding computed column '{Name}' ({ResultType}) = '{Formula}' to archive '{ArchiveRtId}' (tenant '{TenantId}')",
            name, resultType, formula, archiveRtId, Options.Value.TenantId);

        await ServiceClient.AddComputedColumnAsync(Options.Value.TenantId, archiveRtId, name, formula, resultType, indexed);

        Logger.LogInformation("Computed column '{Name}' added and backfilled on archive '{ArchiveRtId}'.",
            name, archiveRtId);
    }
}
