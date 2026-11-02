using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Blueprints;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Blueprints;

/// <summary>
///     Output of the archive activation of a blueprint install or update (AB#7991): which archives of the seed were
///     activated, which were skipped or failed and why, the backfill job of every new rollup, and the warnings of the
///     operation (for example "stream data is not enabled").
/// </summary>
internal static class BlueprintArchiveActivationReport
{
    /// <summary>Logs a summary line, one line per archive that needs attention and every warning of the service.</summary>
    public static void Log(
        ILogger logger,
        IReadOnlyCollection<BlueprintArchiveActivationDto> activations,
        IReadOnlyCollection<string> warnings)
    {
        foreach (var warning in warnings)
        {
            logger.LogWarning("{Warning}", warning);
        }

        if (activations.Count == 0)
        {
            return;
        }

        logger.LogInformation("Archives of the blueprint seed: {Summary}", Summarize(activations));

        foreach (var archive in activations)
        {
            var name = Label(archive);
            switch (archive.Outcome)
            {
                case "Activated":
                    LogBackfill(logger, archive, name);
                    break;
                case "Failed":
                    logger.LogError(
                        "Archive {Archive} could not be activated: {Reason}. It is Failed; retry with RetryArchiveActivation once the cause is fixed.",
                        name, archive.Reason ?? "-");
                    break;
                case "SkippedPreExisting":
                    break; // untouched on purpose; counted in the summary
                default:
                    logger.LogWarning("Archive {Archive} was not activated ({Outcome}): {Reason}",
                        name, archive.Outcome, archive.Reason ?? "-");
                    break;
            }
        }
    }

    /// <summary>One line: the archives per outcome and the queued rollup backfills.</summary>
    internal static string Summarize(IReadOnlyCollection<BlueprintArchiveActivationDto> activations)
    {
        var byOutcome = string.Join(", ",
            activations.GroupBy(a => a.Outcome).Select(g => $"{g.Count()} {g.Key}"));
        var queued = activations.Count(a => a.Backfill == "Queued");
        return queued == 0 ? byOutcome : $"{byOutcome}; {queued} rollup backfill(s) queued";
    }

    private static void LogBackfill(ILogger logger, BlueprintArchiveActivationDto archive, string name)
    {
        switch (archive.Backfill)
        {
            case "Queued":
                logger.LogInformation(
                    "Rollup {Archive} activated; its backfill runs as job {JobId} (follow it with ListRecomputeJobs)",
                    name, archive.BackfillJobRtId ?? "-");
                break;
            case "NothingToBackfill":
                logger.LogInformation("Rollup {Archive} activated; the sources hold no data yet, nothing to backfill", name);
                break;
            case "NotQueued":
                logger.LogWarning("Rollup {Archive} activated, but its backfill was not queued: {Reason}",
                    name, archive.BackfillReason ?? "-");
                break;
            case "Failed":
                logger.LogError("Rollup {Archive} activated, but queueing its backfill failed: {Reason}",
                    name, archive.BackfillReason ?? "-");
                break;
        }
    }

    private static string Label(BlueprintArchiveActivationDto archive) =>
        string.IsNullOrEmpty(archive.WellKnownName)
            ? archive.ArchiveRtId
            : $"{archive.ArchiveRtId} ('{archive.WellKnownName}')";
}
