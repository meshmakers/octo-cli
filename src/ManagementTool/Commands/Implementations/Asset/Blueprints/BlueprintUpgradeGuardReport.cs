using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.CkModelLibraries;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.CkModelCatalog;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Blueprints;

/// <summary>
///     Output of the tenant upgrade guard (CK v2 F2.5, AB#6871) for the blueprint commands: which installed model the
///     CK models of a blueprint would break, why, and the next step. The blocker lines are the ones the library
///     commands print (<see cref="UpgradeGuardReport" />), so a blueprint and an import read the same.
/// </summary>
internal static class BlueprintUpgradeGuardReport
{
    /// <summary>The override of the blueprint commands (the install command's <c>--force</c> already means re-apply).</summary>
    public const string ForceOption = "--forceBreakingUpdate";

    /// <summary>
    ///     The refusal (HTTP 409 <c>CK_UPGRADE_BLOCKED</c>): the blockers and the way forward as log lines, then the
    ///     <see cref="ToolException" /> that ends the command with an error.
    /// </summary>
    public static ToolException Refused(ILogger logger, string operation, string blueprintId,
        CkUpgradeBlockedException blocked, string forceOption)
    {
        logger.LogError("The service refused the {Operation} of '{BlueprintId}'; nothing was changed:", operation,
            blueprintId);
        UpgradeGuardReport.LogBlockers(logger, LogLevel.Error, blocked.Blockers);
        return new ToolException(
            $"The {operation} of {blueprintId} would break {blocked.Blockers.Select(b => b.Dependent).Distinct().Count()} " +
            $"installed CK model(s) (CK_UPGRADE_BLOCKED). Update the dependent models first, or repeat the command with " +
            $"{forceOption} to proceed anyway (needs the data model management role).", blocked);
    }

    /// <summary>The blockers a forced operation went past, as warnings (they become <c>ResolveFailed</c>).</summary>
    public static void LogForced(ILogger logger, string forceOption, IReadOnlyCollection<CkUpgradeBlockerDto> blockers)
    {
        if (blockers.Count == 0)
        {
            return;
        }

        logger.LogWarning(
            "{ForceOption}: applied although the CK models of the blueprint break installed models (they become ResolveFailed):",
            forceOption);
        UpgradeGuardReport.LogBlockers(logger, LogLevel.Warning, blockers);
    }

    /// <summary>The blockers of a preview or a dry run: what a real apply would be refused for.</summary>
    public static void LogWouldBeRefused(ILogger logger, string forceOption,
        IReadOnlyCollection<string> ckUpdateSet, IReadOnlyCollection<CkUpgradeBlockerDto> blockers)
    {
        logger.LogWarning(
            "The CK models of this blueprint ({UpdateSet}) would break {Count} installed model(s); a real apply is " +
            "refused (CK_UPGRADE_BLOCKED) unless it is forced with {ForceOption}:",
            string.Join(", ", ckUpdateSet), blockers.Select(b => b.Dependent).Distinct().Count(), forceOption);
        UpgradeGuardReport.LogBlockers(logger, LogLevel.Warning, blockers);
    }
}
