using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

/// <summary>
/// Queues the backfill of a computed column again (AB#8002): for a column that ended Failed, or that is
/// stuck in Pending / Backfilling without a running job — without removing and re-adding it.
/// </summary>
public class RetryComputedColumnBackfillCommand : ServiceClientOctoCommand<IStreamDataServicesClient>
{
    private readonly IArgument _archiveRtIdArg;
    private readonly IArgument _nameArg;
    private readonly IArgument _waitArg;

    public RetryComputedColumnBackfillCommand(ILogger<RetryComputedColumnBackfillCommand> logger,
        IOptions<OctoToolOptions> options, IStreamDataServicesClient serviceClient,
        IAuthenticationService authenticationService) : base(logger, Constants.AssetRepositoryServicesGroup,
        "RetryComputedColumnBackfill",
        "Queues the backfill of a computed column again — for a column whose backfill Failed, or that is stuck in Pending / Backfilling without a running job — without removing and re-adding it. The backfill runs as a background job on the asset repository; follow it with ListRecomputeJobs or pass -w. Rejected for an Active column. (AB#8002)",
        options, serviceClient, authenticationService)
    {
        _archiveRtIdArg = CommandArgumentValue.AddArgument("id", "identifier",
            ["Runtime id of the archive carrying the computed column"], true, 1);
        _nameArg = CommandArgumentValue.AddArgument("n", "name",
            ["Name of the computed column whose backfill is retried"], true, 1);
        _waitArg = CommandArgumentValue.AddArgument("w", "wait",
            ["Wait until the backfill job has finished; exits non-zero when it fails"], false, 0);
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

        Logger.LogInformation("Retrying the backfill of computed column '{Name}' on archive '{ArchiveRtId}' (tenant '{TenantId}')",
            name, archiveRtId, Options.Value.TenantId);

        var job = await ServiceClient.RetryComputedColumnBackfillAsync(Options.Value.TenantId, archiveRtId, name);
        Logger.LogInformation("Backfill of computed column '{Name}' queued as job {JobId} ({State}).",
            name, job?.RtId ?? "<none>", job?.State ?? "<unknown>");

        if (CommandArgumentValue.IsArgumentUsed(_waitArg))
        {
            if (job is null)
            {
                // Without the job id the newest job of the column could be an older run of it.
                throw new ToolException(
                    $"The asset repository returned no job for the retry of '{name}', so -w cannot follow it. " +
                    $"Check the backfill with ListRecomputeJobs -id {archiveRtId}.");
            }

            await ComputedColumnBackfillWaiter.WaitAsync(ServiceClient, Logger, Options.Value.TenantId, archiveRtId, name,
                PollInterval, job.RtId);
        }
    }
}
