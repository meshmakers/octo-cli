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
    private readonly IArgument _waitArg;
    private readonly IArgument _formulaArg;

    public UpdateComputedColumnFormulaCommand(ILogger<UpdateComputedColumnFormulaCommand> logger,
        IOptions<OctoToolOptions> options, IStreamDataServicesClient serviceClient,
        IAuthenticationService authenticationService) : base(logger, Constants.AssetRepositoryServicesGroup,
        "UpdateComputedColumnFormula",
        "Changes the formula of an existing computed column on an active archive. Readers keep the previous values while the asset repository backfills the new formula as a background job, then switch atomically; a failed job reverts to the previous formula. Returns once the job is queued (follow it with ListRecomputeJobs, or pass -w). Rejected if another computed column references this one, or while a backfill of the column is still running. (AB#4189, AB#8002)",
        options, serviceClient, authenticationService)
    {
        _archiveRtIdArg = CommandArgumentValue.AddArgument("id", "identifier",
            ["Runtime id of the archive carrying the computed column"], true, 1);
        _nameArg = CommandArgumentValue.AddArgument("n", "name",
            ["Name of the computed column to re-formulate"], true, 1);
        _formulaArg = CommandArgumentValue.AddArgument("f", "formula",
            ["The new mXparser formula"], true, 1);
        _waitArg = CommandArgumentValue.AddArgument("w", "wait",
            ["Wait until the background backfill job has finished; exits non-zero when it fails"], false, 0);
    }

    /// <summary>Interval between two status polls with <c>-w</c>. Internal for tests.</summary>
    internal TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

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

        Logger.LogInformation("Formula change of computed column '{Name}' on archive '{ArchiveRtId}' queued; its backfill runs in the background.",
            name, archiveRtId);

        if (CommandArgumentValue.IsArgumentUsed(_waitArg))
        {
            await ComputedColumnBackfillWaiter.WaitAsync(ServiceClient, Logger, Options.Value.TenantId, archiveRtId, name,
                PollInterval);
        }
    }
}
