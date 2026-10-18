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

        foreach (var entry in report.Entries)
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

        var bad = report.Entries.Count(e => !e.IsOk);
        console.WriteLine(report.IsOk
            ? $"Summary: {report.Entries.Count} rollup(s) checked, all consistent."
            : $"Summary: {report.Entries.Count} rollup(s) checked, {bad} inconsistent.");
    }

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
            var job = action.JobId is null ? string.Empty : $"  job={action.JobId}";
            var error = string.IsNullOrEmpty(action.Error) ? string.Empty : $"  error={action.Error}";
            console.WriteLine(
                $"  {action.RollupRtId}  {action.Kind}  {action.Status}{range}{job}{error}  {action.Description}");
        }

        if (!string.IsNullOrEmpty(report.FailedStep))
        {
            console.WriteLine($"Failed step: {report.FailedStep}");
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
