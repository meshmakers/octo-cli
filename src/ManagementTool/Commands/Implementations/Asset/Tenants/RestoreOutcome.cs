using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;

/// <summary>
///     Turns the result of a finished restore job into the end of the command (AB#6969): a restore whose archives failed or
///     whose rollups could not be checked must not be reported as a plain "restored", and the command exits non-zero.
/// </summary>
internal static class RestoreOutcome
{
    public static void ThrowIfWarnings(ILogger logger, string tenantId, string databaseName, JobDto job)
    {
        if (!job.HasWarnings)
        {
            LogInformational(logger, job);
            return;
        }

        var failed = job.FailedArchives ?? [];
        var notChecked = job.NotCheckedRollups ?? [];

        foreach (var archive in failed)
        {
            logger.LogError("Archive '{ArchiveRtId}' failed: {Reason}", archive.ArchiveRtId, archive.Reason ?? "no reason given");
        }

        foreach (var rollup in notChecked)
        {
            if (rollup.IsBlocking)
            {
                logger.LogError("Rollup '{RollupRtId}' NOT CHECKED [{Cause}]: {Reason}", rollup.RollupRtId, rollup.Cause,
                    rollup.Reason ?? "no reason given");
            }
            else
            {
                logger.LogInformation("Rollup '{RollupRtId}' not checked (info) [{Cause}]: {Reason}", rollup.RollupRtId,
                    rollup.Cause, rollup.Reason ?? "no reason given");
            }
        }

        var message = failed.Count > 0
            ? $"Tenant '{tenantId}' (database '{databaseName}') restored with {failed.Count} failed archive(s): " +
              string.Join(", ", failed.Select(a => a.ArchiveRtId))
            : $"Tenant '{tenantId}' (database '{databaseName}') restored with warnings";

        var blocking = notChecked.Where(r => r.IsBlocking).ToList();
        if (blocking.Count > 0)
        {
            message += $"; {blocking.Count} rollup(s) not checked: " +
                       string.Join(", ", blocking.Select(r => $"{r.RollupRtId} [{r.Cause}]"));
        }

        throw new ToolException(message + ". The tenant is not fully restored.");
    }

    private static void LogInformational(ILogger logger, JobDto job)
    {
        foreach (var rollup in job.NotCheckedRollups ?? [])
        {
            logger.LogInformation("Rollup '{RollupRtId}' not checked (info) [{Cause}]: {Reason}", rollup.RollupRtId,
                rollup.Cause, rollup.Reason ?? "no reason given");
        }
    }
}
