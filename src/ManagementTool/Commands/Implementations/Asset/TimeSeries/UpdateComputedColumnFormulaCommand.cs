using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

public class UpdateComputedColumnFormulaCommand : ServiceClientOctoCommand<IStreamDataServicesClient>
{
    private readonly IArgument _archiveRtIdArg;
    private readonly IArgument _nameArg;
    private readonly IArgument _formulaArg;

    public UpdateComputedColumnFormulaCommand(ILogger<UpdateComputedColumnFormulaCommand> logger,
        IOptions<OctoToolOptions> options, IStreamDataServicesClient serviceClient,
        IAuthenticationService authenticationService) : base(logger, Constants.AssetRepositoryServicesGroup,
        "UpdateComputedColumnFormula",
        "Changes the formula of an existing computed column on an active archive. Readers keep the previous values while the new formula is backfilled, then switch atomically. Rejected if another computed column references this one. (AB#4189)",
        options, serviceClient, authenticationService)
    {
        _archiveRtIdArg = CommandArgumentValue.AddArgument("id", "identifier",
            ["Runtime id of the archive carrying the computed column"], true, 1);
        _nameArg = CommandArgumentValue.AddArgument("n", "name",
            ["Name of the computed column to re-formulate"], true, 1);
        _formulaArg = CommandArgumentValue.AddArgument("f", "formula",
            ["The new mXparser formula"], true, 1);
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

        Logger.LogInformation(
            "Changing formula of computed column '{Name}' on archive '{ArchiveRtId}' to '{Formula}' (tenant '{TenantId}')",
            name, archiveRtId, formula, Options.Value.TenantId);

        await ServiceClient.UpdateComputedColumnFormulaAsync(Options.Value.TenantId, archiveRtId, name, formula);

        Logger.LogInformation("Computed column '{Name}' re-formulated and backfilled on archive '{ArchiveRtId}'.",
            name, archiveRtId);
    }
}
