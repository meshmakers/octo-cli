using GraphQlDtos.DataTransferObjects.System.StreamData.v1;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.Methods;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

public class BackfillRollupCommand : ServiceClientOctoCommand<IStreamDataServicesClient>
{
    // AB#4286: the backfill is a durable background job — the command returns the Pending job id at once instead of
    // blocking for minutes. --wait lets a user opt into following the job to completion.
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollTimeout = TimeSpan.FromMinutes(30);

    private readonly ArchiveMethodCall _methods;
    private readonly IArgument _rollupRtIdArg;
    private readonly IArgument _waitArg;

    public BackfillRollupCommand(ILogger<BackfillRollupCommand> logger,
        IOptions<OctoToolOptions> options, IStreamDataServicesClient serviceClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService) : base(logger, Constants.AssetRepositoryServicesGroup,
        "BackfillRollup",
        "Queues a durable background backfill that populates / resets a rollup over the ENTIRE history of its source archive without supplying a timestamp (AB#4269 / AB#4286). Resolves the source archive's earliest timestamp and enqueues a recompute of [sourceMin, now); the heavy work runs in the background so it is never cancelled by a client timeout. Returns the Pending job id immediately. Use -w/--wait to poll until the job completes. A no-op when the source archive holds no data.",
        options, serviceClient, authenticationService)
    {
        _methods = new ArchiveMethodCall(tenantClient, authenticationService, logger);
        _rollupRtIdArg = CommandArgumentValue.AddArgument("id", "identifier",
            ["Runtime id of the CkRollupArchive to backfill from its source"], true, 1);
        _waitArg = CommandArgumentValue.AddArgument("w", "wait",
            ["Poll the recompute job until it completes (or fails) instead of returning immediately"], false, 0);
    }

    /// <summary>The delay between two reads of the operation or the job list (tests shorten it).</summary>
    internal TimeSpan PollInterval { get; set; } = DefaultPollInterval;

    /// <summary>How long the command waits for the operation to name its queued job before it gives up on the id.</summary>
    internal TimeSpan QueuedTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>The clock of the waits (tests).</summary>
    internal TimeProvider TimeProvider { get; set; } = TimeProvider.System;

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

        // BackfillFromSource-2 answers an operation handle at once; the operation names the queued job (progress stage
        // QUEUED, parameter jobRtId) and runs until the backfill ended.
        ArchiveMethodCall.RequireObjectId(rollupRtId, "RollupRtId");
        var handle = await _methods.RunAsync(RtRollupArchiveMethodClient.BackfillFromSource2MethodId,
                () => new RtRollupArchiveMethodClient(_methods.TenantClient).BackfillFromSource2Async(rollupRtId))
            ?? throw new ServiceClientException(
                $"The CK method {RtRollupArchiveMethodClient.BackfillFromSource2MethodId} returned no operation handle.");
        Logger.LogDebug("Backfill of rollup '{RollupRtId}' accepted as operation {OperationId}", rollupRtId,
            handle.OperationId);

        var queued = await ReadQueuedJobAsync(handle, tenantId, rollupRtId);
        if (queued.NoData)
        {
            Logger.LogInformation(
                "Backfill of rollup '{RollupRtId}' was a no-op: the source archive holds no data.",
                rollupRtId);
            return;
        }

        var wait = CommandArgumentValue.IsArgumentUsed(_waitArg);
        if (queued.Final is not null)
        {
            // The backfill ended before the first read: no "runs in the background", the final job line at once.
            LogJob(queued.Final);
            return;
        }

        if (queued.JobRtId is null)
        {
            Logger.LogWarning(
                "Backfill of rollup '{RollupRtId}' was accepted as operation {OperationId}, but it named no job within "
                + "{Timeout}s. It runs in the background — poll with 'ListRecomputeJobs -id <rollupRtId>'.",
                rollupRtId, handle.OperationId, (int)QueuedTimeout.TotalSeconds);
            if (wait)
            {
                // -w was asked for: keep waiting on the operation itself.
                await WaitForJobAsync(tenantId, rollupRtId, handle, null);
            }

            return;
        }

        Logger.LogInformation(
            "Backfill of rollup '{RollupRtId}' queued as job {RtId} (state={State}). It runs in the background — "
            + "poll with 'ListRecomputeJobs -id <rollupRtId>'.",
            rollupRtId, queued.JobRtId, queued.State);

        if (!wait)
        {
            return;
        }

        await WaitForJobAsync(tenantId, rollupRtId, handle, queued.JobRtId);
    }

    /// <summary>
    ///     Reads the operation until it names its job (stage <c>QUEUED</c>) or is final. A final operation without a job is
    ///     the no-op of a source archive without data.
    /// </summary>
    private async Task<QueuedJob> ReadQueuedJobAsync(CkOperationHandle<RtRecomputeJobInfoRecordDto?> handle,
        string tenantId, string rollupRtId)
    {
        string? jobRtId = null;
        using var named = new CancellationTokenSource();
        var progress = new SyncProgress(operation =>
        {
            if (operation.Progress?.Parameters.TryGetValue("jobRtId", out var id) == true && !string.IsNullOrEmpty(id))
            {
                jobRtId = id;
                named.Cancel();
            }
        });
        CkOperation operation;
        try
        {
            operation = await new CkOperationsClient(_methods.TenantClient).WaitForCompletionAsync(handle, progress,
                WaitOptions(QueuedTimeout), named.Token);
        }
        catch (OperationCanceledException) when (jobRtId is not null)
        {
            return new QueuedJob(jobRtId, "Pending", false, null);
        }
        catch (CkOperationWaitTimeoutException)
        {
            return new QueuedJob(null, null, false, null);
        }

        if (operation.State == CkOperationState.Failed)
        {
            // The job ended Failed before the first read; its id is a parameter of the error.
            var failedId = operation.Error?.Parameters.GetValueOrDefault("jobRtId");
            if (operation.Error?.Code == ArchiveMethodCall.RecomputeFailedCode && !string.IsNullOrEmpty(failedId))
            {
                return new QueuedJob(failedId, "Failed", false, await ArchiveMethodCall.FailedJobAsync(ServiceClient,
                    tenantId, rollupRtId, operation.Error, EnsureAuthenticatedAsync));
            }

            throw new ServiceClientException(operation.Error is null
                ? $"The backfill operation {handle.OperationId} failed without an error code."
                : CkMethodErrorText.Format(operation.Error));
        }

        var job = handle.ReadResult(operation);
        return job is null
            ? new QueuedJob(null, null, true, null)
            : new QueuedJob(job.JobRtId, job.State?.ToString() ?? "Unknown", false, ArchiveMethodCall.ToJobInfo(job));
    }

    /// <summary>
    ///     Follows the operation until the backfill ended and prints the job as the REST answer did. When the operation ends
    ///     without an outcome (<c>METHOD_OUTCOME_UNKNOWN</c>: the asset repository restarted; the queued work goes on),
    ///     the wait continues on the archive's recompute jobs.
    /// </summary>
    private async Task WaitForJobAsync(string tenantId, string rollupRtId,
        CkOperationHandle<RtRecomputeJobInfoRecordDto?> handle, string? jobRtId)
    {
        Logger.LogInformation("Waiting for backfill job {RtId} to complete (poll every {Interval}s, timeout {Timeout}min)...",
            jobRtId ?? handle.OperationId, (int)PollInterval.TotalSeconds, (int)PollTimeout.TotalMinutes);

        RollupRecomputeJobInfoDto? job;
        try
        {
            var result = await _methods.WaitForJobAsync(handle, PollTimeout, TimeProvider, PollInterval);
            job = result.IsSuccess
                ? result.Value is null ? null : ArchiveMethodCall.ToJobInfo(result.Value)
                : await ArchiveMethodCall.FailedJobAsync(ServiceClient, tenantId, rollupRtId, result.Error!,
                    EnsureAuthenticatedAsync);
        }
        catch (CkOperationWaitTimeoutException)
        {
            Logger.LogWarning(
                "Timed out after {Timeout}min waiting for backfill job {RtId}; it may still be running in the "
                + "background. Check with 'ListRecomputeJobs -id <rollupRtId>'.",
                (int)PollTimeout.TotalMinutes, jobRtId ?? handle.OperationId);
            return;
        }
        catch (CkMethodCallException e) when (e.Code == ArchiveMethodCall.OutcomeUnknownCode && jobRtId is null)
        {
            // Without a job id there is nothing to follow in the job list; the queued work goes on.
            Logger.LogWarning(
                "The backfill operation {OperationId} ended without an outcome ({Code}, asset repository restart?); the "
                + "backfill may still run in the background. Check with 'ListRecomputeJobs -id <rollupRtId>'.",
                handle.OperationId, e.Code);
            return;
        }
        catch (CkMethodCallException e) when (e.Code == ArchiveMethodCall.OutcomeUnknownCode && jobRtId is not null)
        {
            Logger.LogDebug("The backfill operation ended without an outcome ({Code}); following job {RtId} in the job list.",
                e.Code, jobRtId);
            // The stream data client's token is refreshed before every poll: this follows a wait of up to 30 minutes.
            var (pollResult, polled) = await PollUntilTerminalAsync(
                async () =>
                {
                    await EnsureAuthenticatedAsync();
                    return await ServiceClient.ListRecomputeJobsForArchiveAsync(tenantId, rollupRtId);
                },
                jobRtId, () => DateTime.UtcNow, Task.Delay, PollInterval, PollTimeout);
            switch (pollResult)
            {
                case BackfillPollResult.Terminal when polled is not null:
                    job = polled;
                    break;
                case BackfillPollResult.Vanished:
                    Logger.LogWarning("Backfill job {RtId} no longer listed for rollup '{RollupRtId}'; stopping wait.",
                        jobRtId, rollupRtId);
                    return;
                default:
                    Logger.LogWarning(
                        "Timed out after {Timeout}min waiting for backfill job {RtId}; it may still be running in the "
                        + "background. Check with 'ListRecomputeJobs -id <rollupRtId>'.",
                        (int)PollTimeout.TotalMinutes, jobRtId);
                    return;
            }
        }

        if (job is null)
        {
            Logger.LogWarning("Backfill job {RtId} reached a terminal state, but no job snapshot was returned.",
                jobRtId ?? handle.OperationId);
            return;
        }

        LogJob(job);
    }

    private void LogJob(RollupRecomputeJobInfoDto job)
    {
        Logger.LogInformation(
            "Backfill job {RtId}: state={State}, rows={Rows}, windows={Windows}, duration={Duration}ms, " +
            "lastProgress={LastProgress}, error={Error}",
            job.RtId, job.State,
            job.RowsProcessed?.ToString() ?? "n/a",
            job.WindowsProcessed?.ToString() ?? "n/a",
            job.DurationMs?.ToString() ?? "n/a",
            job.LastProgressAt?.ToString("O") ?? "n/a",
            job.ErrorReason ?? "none");
    }

    private CkOperationWaitOptions WaitOptions(TimeSpan timeout) => new()
    {
        Timeout = timeout,
        TimeProvider = TimeProvider,
        DefaultPollInterval = PollInterval,
        MinPollInterval = PollInterval < TimeSpan.FromSeconds(1) ? PollInterval : TimeSpan.FromSeconds(1),
        BeforeReadAsync = _ => _methods.EnsureAuthenticatedAsync()
    };

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
    /// Pure, testable poll loop (AB#4286) over the archive's recompute jobs: inspects the target job until it reaches a
    /// terminal state, disappears, or the timeout elapses. Since the backfill runs as an operation, the wait follows the
    /// operation; this loop takes over when the operation ended without an outcome (the asset repository restarted)
    /// while the queued job goes on.
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

    /// <summary>The queued job: its id and state, the no-op, or the final job when the backfill ended before the first read.</summary>
    private sealed record QueuedJob(string? JobRtId, string? State, bool NoData, RollupRecomputeJobInfoDto? Final);

    /// <summary>An <see cref="IProgress{T}" /> that reports synchronously, so the job id is known before the next read.</summary>
    private sealed class SyncProgress(Action<CkOperation> report) : IProgress<CkOperation>
    {
        public void Report(CkOperation value) => report(value);
    }
}
