using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.CkModelCatalog;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.CkModelLibraries;

/// <summary>
///     Prints the tenant upgrade guard result (CK v2 F2.5, AB#6870) of the import commands: which installed model an
///     update set would break, why, and the proven next step. Shared by <c>ImportFromCatalog</c> and <c>FixAll</c>.
/// </summary>
internal static class UpgradeGuardReport
{
    public static void LogBlockers(ILogger logger, LogLevel level, IEnumerable<CkUpgradeBlockerDto> blockers,
        string indent = "  ")
    {
        foreach (var blocker in blockers)
        {
            logger.Log(level, "{Indent}{Dependent} would break ({Kind} {Subject}) through {Update}: {Message}", indent,
                blocker.Dependent, blocker.Kind, blocker.Subject, blocker.Update, blocker.Message);
            if (!string.IsNullOrEmpty(blocker.ResolveHint))
            {
                logger.Log(level, "{Indent}  -> {Hint}", indent, blocker.ResolveHint);
            }
        }
    }
}
