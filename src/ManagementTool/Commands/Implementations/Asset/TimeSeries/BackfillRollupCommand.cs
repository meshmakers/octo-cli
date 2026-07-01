using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

public class BackfillRollupCommand : ServiceClientOctoCommand<IStreamDataServicesClient>
{
    // AB#4286: the backfill is now a durable background job — it returns a Pending job id immediately
    // instead of blocking for minutes. --wait lets a user opt into polling the job to completion.
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollTimeout = TimeSpan.FromMinutes(30);

    private readonly IArgument _rollupRtIdArg;
    private readonly IArgument _waitArg;

    public BackfillRollupCommand(ILogger<BackfillRollupCommand> logger,
        IOptions<OctoToolOptions> options, IStreamDataServicesClient serviceClient,
        IAuthenticationService authenticationService) : base(logger, Constants.AssetRepositoryServicesGroup,
        "BackfillRollup",
        "Queues a durable background backfill that populates / resets a rollup over the ENTIRE history of its source archive without supplying a timestamp (AB#4269 / AB#4286). Resolves the source archive's earliest timestamp and enqueues a recompute of [sourceMin, now); the heavy work runs in the background so it is never cancelled by a client timeout. Returns the Pending job id immediately. Use -w/--wait to poll until the job completes. A no-op when the source archive holds no data.",
        options, serviceClient, authenticationService)
    {
        _rollupRtIdArg = CommandArgumentValue.AddArgument("id", "identifier",
            ["Runtime id of the CkRollupArchive to backfill from its source"], true, 1);
        _waitArg = CommandArgumentValue.AddArgument("w", "wait",
            ["Poll the recompute job until it completes (or fails) instead of returning immediately"], false, 0);
    }

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        var tenantId = Options.Value.TenantId;
        var rollupRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_rollupRtIdArg);

        Logger.LogInformation(
            "Triggering backfill-from-source for rollup '{RollupRtId}' (tenant '{TenantId}')",
            rollupRtId, tenantId);

        var job = await ServiceClient.BackfillRollupFromSourceAsync(tenantId, rollupRtId);

        if (job is null)
        {
            Logger.LogInformation(
                "Backfill of rollup '{RollupRtId}' was a no-op: the source archive holds no data.",
                rollupRtId);
            return;
        }

        Logger.LogInformation(
            "Backfill of rollup '{RollupRtId}' queued as job {RtId} (state={State}). It runs in the background — "
            + "poll with 'ListRecomputeJobs -id <rollupRtId>'.",
            rollupRtId, job.RtId, job.State);

        if (!CommandArgumentValue.IsArgumentUsed(_waitArg))
        {
            return;
        }

        await WaitForJobAsync(tenantId, rollupRtId, job.RtId);
    }

    /// <summary>
    /// Polls the rollup's recompute-jobs list until the target job leaves the Pending/Running/Swapping
    /// states (i.e. reaches Completed/Failed) or the bounded timeout elapses.
    /// </summary>
    private async Task WaitForJobAsync(string tenantId, string rollupRtId, string jobRtId)
    {
        Logger.LogInformation("Waiting for backfill job {RtId} to complete (poll every {Interval}s, timeout {Timeout}min)...",
            jobRtId, (int)PollInterval.TotalSeconds, (int)PollTimeout.TotalMinutes);

        var (result, job) = await PollUntilTerminalAsync(
            () => ServiceClient.ListRecomputeJobsForArchiveAsync(tenantId, rollupRtId),
            jobRtId, () => DateTime.UtcNow, Task.Delay, PollInterval, PollTimeout);

        switch (result)
        {
            case BackfillPollResult.Terminal:
                Logger.LogInformation(
                    "Backfill job {RtId}: state={State}, rows={Rows}, windows={Windows}, duration={Duration}ms, error={Error}",
                    job!.RtId, job.State,
                    job.RowsProcessed?.ToString() ?? "n/a",
                    job.WindowsProcessed?.ToString() ?? "n/a",
                    job.DurationMs?.ToString() ?? "n/a",
                    job.ErrorReason ?? "none");
                break;
            case BackfillPollResult.Vanished:
                Logger.LogWarning("Backfill job {RtId} no longer listed for rollup '{RollupRtId}'; stopping wait.",
                    jobRtId, rollupRtId);
                break;
            default:
                Logger.LogWarning(
                    "Timed out after {Timeout}min waiting for backfill job {RtId}; it may still be running in the "
                    + "background. Check with 'ListRecomputeJobs -id <rollupRtId>'.",
                    (int)PollTimeout.TotalMinutes, jobRtId);
                break;
        }
    }

    /// <summary>
    /// Terminal outcome of <see cref="PollUntilTerminalAsync"/>.
    /// </summary>
    public enum BackfillPollResult
    {
        /// <summary>The job reached a terminal state (Completed / Failed).</summary>
        Terminal,

        /// <summary>The job id was no longer present in the listing (e.g. pruned).</summary>
        Vanished,

        /// <summary>The bounded timeout elapsed while the job was still non-terminal.</summary>
        TimedOut,
    }

    /// <summary>
    /// Pure, testable poll loop (AB#4286): repeatedly lists the recompute jobs and inspects the target
    /// job until it reaches a terminal state, disappears, or the timeout elapses. The clock, delay, and
    /// job-list source are injected so the logic is unit-coverable without real time or a live service.
    /// </summary>
    public static async Task<(BackfillPollResult Result, RollupRecomputeJobInfoDto? Job)> PollUntilTerminalAsync(
        Func<Task<IReadOnlyList<RollupRecomputeJobInfoDto>>> listJobs,
        string jobRtId,
        Func<DateTime> utcNow,
        Func<TimeSpan, Task> delay,
        TimeSpan pollInterval,
        TimeSpan timeout)
    {
        var deadline = utcNow() + timeout;
        while (utcNow() < deadline)
        {
            await delay(pollInterval);

            var jobs = await listJobs();
            var current = jobs.FirstOrDefault(j => j.RtId == jobRtId);
            if (current is null)
            {
                return (BackfillPollResult.Vanished, null);
            }

            if (IsTerminal(current.State))
            {
                return (BackfillPollResult.Terminal, current);
            }
        }

        return (BackfillPollResult.TimedOut, null);
    }

    private static bool IsTerminal(string state) =>
        state.Equals("Completed", StringComparison.OrdinalIgnoreCase)
        || state.Equals("Failed", StringComparison.OrdinalIgnoreCase);
}
