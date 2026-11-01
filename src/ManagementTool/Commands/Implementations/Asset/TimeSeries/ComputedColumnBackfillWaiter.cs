using System.Net;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

/// <summary>
/// Follows the background backfill job of a computed column until it ends (AB#8002). The server queues
/// the backfill and returns at once; this polls the archive's recompute jobs (short requests, so no HTTP
/// timeout can hit) and reports progress until the newest job of the column is Completed or Failed.
/// </summary>
internal static class ComputedColumnBackfillWaiter
{
    /// <summary>How long the asset repository may answer 502/503/504 in a row before the wait gives up.</summary>
    internal static TimeSpan TransientTolerance { get; set; } = TimeSpan.FromMinutes(5);

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    /// <summary>Waits for the newest backfill job of <paramref name="column"/>; throws when it failed.</summary>
    /// <remarks>
    /// <paramref name="ensureAuthenticated"/> runs before every poll: a backfill of a large archive takes longer
    /// than an access token lives (test-2, 2026-11-01: the wait stopped with "Access to the requested resource is
    /// not allowed" after ~9 minutes while the backfill went on).
    /// </remarks>
    public static async Task WaitAsync(IStreamDataServicesClient client, ILogger logger, string tenantId,
        string archiveRtId, string column, TimeSpan pollInterval, Func<Task> ensureAuthenticated, string? jobRtId = null)
    {
        string? lastLine = null;
        DateTime? unavailableSince = null;
        while (true)
        {
            await ensureAuthenticated();
            IReadOnlyList<RollupRecomputeJobInfoDto> jobs;
            try
            {
                jobs = await client.ListRecomputeJobsForArchiveAsync(tenantId, archiveRtId);
                unavailableSince = null;
            }
            catch (ServiceClientResultException ex) when (IsTransient(ex.HttpStatusCode))
            {
                // A rollout of the asset repository answers 502/503/504 for a while; the backfill job is handed back
                // and resumed by the new pod, so keep waiting (test-2, 2026-11-01: -w stopped on a 503 mid-rollout).
                unavailableSince ??= DateTime.UtcNow;
                if (DateTime.UtcNow - unavailableSince > TransientTolerance)
                {
                    throw new ToolException(
                        $"The asset repository has been unavailable for more than {TransientTolerance.TotalMinutes:0} minutes " +
                        $"({(int)ex.HttpStatusCode}). The backfill of '{column}' goes on in the background; " +
                        $"check it later with ListRecomputeJobs -id {archiveRtId}.", ex);
                }

                logger.LogWarning("Asset repository unavailable ({Status}), probably a rollout; still waiting for '{Column}'.",
                    (int)ex.HttpStatusCode, column);
                await Task.Delay(pollInterval);
                continue;
            }

            var job = jobRtId is not null
                ? jobs.FirstOrDefault(j => j.RtId == jobRtId)
                : jobs.FirstOrDefault(j => string.Equals(j.ComputedColumnName, column, StringComparison.Ordinal));

            if (job is null)
            {
                throw new ToolException(
                    $"No backfill job found for computed column '{column}' on archive '{archiveRtId}'. " +
                    "The asset repository may be older than AB#8002 (it backfilled inside the call).");
            }

            var line = $"Backfill of '{column}': {job.State}, rows={job.RowsProcessed?.ToString() ?? "0"}";
            if (line != lastLine)
            {
                logger.LogInformation("{Progress}", line);
                lastLine = line;
            }

            switch (job.State)
            {
                case "Completed":
                    logger.LogInformation("Computed column '{Column}' on archive '{ArchiveRtId}' is backfilled ({Rows} rows, {Duration} ms).",
                        column, archiveRtId, job.RowsProcessed, job.DurationMs);
                    return;
                case "Failed":
                    throw new ToolException(
                        $"Backfill of computed column '{column}' failed: {job.ErrorReason ?? "no reason given"}. " +
                        $"Retry it with RetryComputedColumnBackfill -id {archiveRtId} -n {column}.");
            }

            await Task.Delay(pollInterval);
        }
    }
}
