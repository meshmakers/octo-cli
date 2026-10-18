using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.CkModelCatalog;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.CkModelLibraries;

/// <summary>
/// AB#6869: renders the tenant upgrade guard result (AB#6868 <see cref="CkUpgradeBlockerDto" />) for
/// <c>LibraryStatus</c> and <c>CheckUpgrade</c>. Null-safe: an asset repository that predates the members
/// delivers no blockers, and a blocker with missing text still renders from its other members.
/// </summary>
internal static class UpgradeBlockerFormatter
{
    /// <summary>One-line explanation of a blocker: <c>Dependent: message</c>.</summary>
    public static string Describe(CkUpgradeBlockerDto blocker)
    {
        var text = !string.IsNullOrWhiteSpace(blocker.Message)
            ? blocker.Message
            : string.Join(' ', new[] { blocker.Kind, blocker.Subject }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var dependent = string.IsNullOrWhiteSpace(blocker.Dependent) ? "(unknown model)" : blocker.Dependent;
        return string.IsNullOrWhiteSpace(text) ? dependent : $"{dependent}: {text}";
    }

    /// <summary>
    /// REASON cell of a blocked <c>LibraryStatus</c> row: the first blocker, "(+N more)" and the resolve hint of
    /// the first blocker when the server proved one.
    /// </summary>
    public static string Reason(IReadOnlyList<CkUpgradeBlockerDto>? blockers)
    {
        if (blockers == null || blockers.Count == 0)
            return "Would break an installed model";

        var first = blockers[0];
        var reason = Describe(first);
        if (blockers.Count > 1)
            reason += $" (+{blockers.Count - 1} more)";
        if (!string.IsNullOrWhiteSpace(first.ResolveHint))
            reason += $"; {first.ResolveHint}";
        return reason;
    }

    /// <summary>Lines of a "Would break:" entry, one per blocker plus an indented resolve hint when present.</summary>
    public static IEnumerable<string> DetailLines(IReadOnlyList<CkUpgradeBlockerDto>? blockers)
    {
        foreach (var blocker in blockers ?? [])
        {
            var inSet = blocker.DependentIsInUpdateSet ? " (part of this update set)" : string.Empty;
            yield return $"  - {Describe(blocker)}{inSet}";
            if (!string.IsNullOrWhiteSpace(blocker.ResolveHint))
                yield return $"      resolve: {blocker.ResolveHint}";
        }
    }
}
