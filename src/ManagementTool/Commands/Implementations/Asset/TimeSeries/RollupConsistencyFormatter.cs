using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

/// <summary>
///     Prints the rollup validate / repair reports (AB#6968) the same way for <c>ValidateRollups</c> and
///     <c>RepairRollups</c>. Only fields of the server report are printed.
/// </summary>
internal static class RollupConsistencyFormatter
{
    public static void Print(IConsoleService console, RollupConsistencyReportDto report, string title)
    {
        console.WriteLine(title);
        if (report.Entries.Count == 0)
        {
            console.WriteLine("  No rollup archives were checked.");
        }

        foreach (var entry in report.Entries.Where(e => !e.IsNotChecked || e.Findings.Count > 0))
        {
            if (entry.IsOk)
            {
                console.WriteLine($"  {entry.RollupRtId}  OK");
                continue;
            }

            console.WriteLine($"  {entry.RollupRtId}  {entry.Findings.Count} finding(s)");
            foreach (var finding in entry.Findings)
            {
                console.WriteLine($"    [{finding.Check}] {finding.Message}{Describe(finding)}");
            }
        }

        PrintNotChecked(console, report);

        var blocking = report.Entries.Count(e => e.IsBlockedByNotChecked);
        var informational = report.Entries.Count(e => e.IsNotChecked && !e.IsBlockedByNotChecked);
        var bad = report.Entries.Count(e => e.Findings.Count > 0);
        var info = informational > 0 ? $", {informational} not checked (info)" : string.Empty;
        console.WriteLine(report.IsOk
            ? $"Summary: {report.Entries.Count} rollup(s) listed, all consistent{info}."
            : $"Summary: {report.Entries.Count} rollup(s) listed, {bad} inconsistent, {blocking} NOT CHECKED{info}. " +
              "A rollup that is not checked is not known to be consistent.");
    }

    /// <summary>
    ///     Prints the rollups the validator could not check as their own sections, with the cause and the reason
    ///     (AB#6968): blocking gaps (problems, the report is not ok) and informational ones (a rollup that was never
    ///     activated has no data; the report stays ok). Prints nothing when every rollup was checked.
    /// </summary>
    public static void PrintNotChecked(IConsoleService console, RollupConsistencyReportDto report)
    {
        var blocking = report.Entries.Where(e => e.IsBlockedByNotChecked).ToList();
        if (blocking.Count > 0)
        {
            console.WriteLine($"Not checked ({blocking.Count}):");
            foreach (var entry in blocking)
            {
                console.WriteLine($"  {entry.RollupRtId}  NOT CHECKED [{Cause(entry)}] {Reason(entry)}");
            }
        }

        var informational = report.Entries.Where(e => e.IsNotChecked && !e.IsBlockedByNotChecked).ToList();
        if (informational.Count > 0)
        {
            console.WriteLine($"Not checked (informational, {informational.Count}):");
            foreach (var entry in informational)
            {
                console.WriteLine($"  {entry.RollupRtId}  not checked (info) [{Cause(entry)}] {Reason(entry)}");
            }
        }
    }

    private static string Cause(RollupConsistencyEntryDto entry) => entry.NotChecked?.Cause ?? "Unknown";

    private static string Reason(RollupConsistencyEntryDto entry) => entry.NotChecked?.Reason ?? "The server did not say why.";

    public static void Print(IConsoleService console, RollupRepairReportDto report)
    {
        console.WriteLine(report.DryRun
            ? "Rollup repair DRY RUN - nothing was changed. Planned steps:"
            : "Rollup repair APPLIED. Steps:");
        if (report.Actions.Count == 0)
        {
            console.WriteLine("  Nothing to repair.");
        }

        foreach (var action in report.Actions)
        {
            var range = action.RangeFromUtc is null && action.RangeToUtc is null
                ? string.Empty
                : $"  [{action.RangeFromUtc:O}, {action.RangeToUtc:O})";
            var error = string.IsNullOrEmpty(action.Error) ? string.Empty : $"  error={action.Error}";
            console.WriteLine(
                $"  {action.RollupRtId}  {action.Kind}  {action.Status}{range}{error}  {action.Description}");
        }

        if (!string.IsNullOrEmpty(report.FailedStep))
        {
            console.WriteLine($"Failed step: {report.FailedStep}");
        }

        // A rollup the validator could not check is not repaired either: say so instead of leaving it out.
        if (report.Before.HasNotChecked)
        {
            console.WriteLine("Skipped, not repaired because they could not be checked:");
            PrintNotChecked(console, report.Before);
        }
    }

    public static void Print(IConsoleService console, RollupRepairRequestDto request)
    {
        console.WriteLine(
            $"Rollup repair request {request.RequestId}: {request.State} (requested {request.RequestedAtUtc:O}" +
            (request.FinishedAtUtc is { } finished ? $", finished {finished:O})" : ")"));

        if (request.Report is { } report)
        {
            Print(console, report);
            if (report.After is { } after)
            {
                Print(console, after, "Rollup validation after the repair:");
            }

            return;
        }

        console.WriteLine(request.PlannedActions.Count == 0
            ? "  Planned steps: none."
            : "  Planned steps:");
        foreach (var action in request.PlannedActions)
        {
            console.WriteLine($"  {action.RollupRtId}  {action.Kind}  {action.Description}");
        }
    }

    private static string Describe(RollupConsistencyFindingDto finding)
    {
        var parts = new List<string>();
        if (finding.RangeFromUtc is not null || finding.RangeToUtc is not null)
        {
            parts.Add($"range [{finding.RangeFromUtc:O}, {finding.RangeToUtc:O})");
        }

        if (finding.Generation is not null)
        {
            parts.Add($"generation {finding.Generation}");
        }

        if (finding.Rows is not null)
        {
            parts.Add($"rows {finding.Rows}");
        }

        if (!string.IsNullOrEmpty(finding.Gap))
        {
            parts.Add($"gap {finding.Gap}");
        }

        return parts.Count == 0 ? string.Empty : "  (" + string.Join(", ", parts) + ")";
    }
}

/// <summary>Parses the optional comma-separated rollup id argument shared by the rollup consistency commands.</summary>
internal static class RollupIdList
{
    public static IReadOnlyCollection<string>? Parse(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var ids = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (ids.Length == 0)
        {
            throw new ToolException("-id must contain at least one rollup runtime id.");
        }

        return ids;
    }
}

/// <summary>Maps the end state of a rollup repair request to the exit code of the commands (AB#6968).</summary>
internal static class RollupRepairRequestOutcome
{
    public static void ThrowIfFailed(RollupRepairRequestDto request)
    {
        if (!request.State.Equals("Failed", StringComparison.OrdinalIgnoreCase) &&
            request.Report is not { IsOk: false })
        {
            return;
        }

        var step = string.IsNullOrEmpty(request.Report?.FailedStep) ? string.Empty : $" at step: {request.Report!.FailedStep}";
        throw new ToolException($"Rollup repair request '{request.RequestId}' failed{step}.");
    }
}
