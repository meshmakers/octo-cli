using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.CkModelLibraries;

internal class LibraryStatusCommand : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IArgument _needsActionArg;
    private readonly IArgument _installedOnlyArg;

    public LibraryStatusCommand(ILogger<LibraryStatusCommand> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "LibraryStatus",
            "Shows installed CK model libraries with catalog availability. Use --needs-action to filter.",
            options, assetServicesClient, authenticationService)
    {
        _needsActionArg = CommandArgumentValue.AddArgument("na", "needs-action",
            ["Show only models that need action"], false, 0);
        _installedOnlyArg = CommandArgumentValue.AddArgument("io", "installed-only",
            ["Show only installed models"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [], description: "Full status"),
                new CodeSample(arguments: [new CodeSampleArgument(_needsActionArg)],
                    description: "Only models needing attention",
                    expectedOutput: """
                    NAME                      INSTALLED    CATALOG      STATE           ACTION               REASON
                    ------------------------- ------------ ------------ --------------- -------------------- ------
                    System                    2.0.7        2.0.7        Available       Service-Managed      -
                    Basic                     2.0.2        2.0.2        Available       -                    -
                    Loxone                    5.2.0        5.3.0        ResolveFailed   Fix                  Unmet dependency: System.Communication-[4.5,5.0) (floor 4.5.0): installed System.Communication-4.4.0
                    Industry.Energy           -            2.0.0        Not Installed   Install              -
                    """),
                new CodeSample(arguments: [],
                    description: "Blocked update (AB#6869): the offered catalog version would break an installed model. " +
                                 "A blocked row keeps its update but is not 'needs action', so --needs-action does not list it",
                    expectedOutput: """
                    2 model(s) (0 need action, 1 blocked):
                    NAME                      INSTALLED    CATALOG      STATE           ACTION               REASON
                    ------------------------- ------------ ------------ --------------- -------------------- ------
                    Basic                     2.4.0        2.5.0        Available       Blocked              EnergyCommunity.Registration-2.8.0: exact pin Basic-2.4.0: Basic-2.5.0 replaces it (+1 more); update EnergyCommunity.Registration to 2.10.0 first
                    System                    2.0.7        2.0.7        Available       Service-Managed      -

                    Would break:
                    Basic 2.4.0 -> 2.5.0
                      - EnergyCommunity.Registration-2.8.0: exact pin Basic-2.4.0: Basic-2.5.0 replaces it
                          resolve: update EnergyCommunity.Registration to 2.10.0 first
                      - EnergyCommunity.Billing-1.3.0: exact pin Basic-2.4.0: Basic-2.5.0 replaces it
                    """),
            ]
        );

    public override async Task Execute()
    {
        var tenantId = Options.Value.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
            throw ToolException.NoTenantIdConfigured();

        var status = await ServiceClient.GetLibraryStatusAsync(tenantId);
        var items = status.Items;

        if (CommandArgumentValue.IsArgumentUsed(_needsActionArg))
            items = items.Where(i => i.NeedsAction).ToList();
        if (CommandArgumentValue.IsArgumentUsed(_installedOnlyArg))
            items = items.Where(i => i.InstalledVersion != null).ToList();

        // AB#6869: a blocked row (offered update would break an installed model) keeps HasUpdate=true but is not
        // NeedsAction, so --needs-action does not list it. The summary counts the blocked rows of the answer.
        var blockedCount = status.Items.Count(i => i.IsBlocked);
        if (blockedCount > 0)
            Logger.LogInformation("{Count} model(s) ({NeedsAction} need action, {Blocked} blocked):", items.Count,
                status.ModelsNeedingActionCount, blockedCount);
        else
            Logger.LogInformation("{Count} model(s) ({NeedsAction} need action):", items.Count, status.ModelsNeedingActionCount);
        // AB#6674: REASON is last (it is free text and long): why a ResolveFailed model failed, "-" otherwise.
        Logger.LogInformation("{N,-25} {I,-12} {C,-12} {S,-15} {A,-20} {R}", "NAME", "INSTALLED", "CATALOG", "STATE", "ACTION", "REASON");
        Logger.LogInformation("{S1,-25} {S2,-12} {S3,-12} {S4,-15} {S5,-20} {S6}", new string('-', 25), new string('-', 12), new string('-', 12), new string('-', 15), new string('-', 20), new string('-', 6));

        foreach (var item in items)
        {
            var action = item.IsServiceManaged ? "Service-Managed"
                : !item.IsCompatible ? "Incompatible"
                : item.IsBlocked ? "Blocked"
                : item.NeedsAction ? (item.HasUpdate ? "Update" : "Fix")
                : item.InstalledVersion == null ? "Install"
                : "-";
            var reason = action == "Blocked" ? UpgradeBlockerFormatter.Reason(item.Blockers)
                : string.IsNullOrWhiteSpace(item.ResolveFailedReason) ? "-" : item.ResolveFailedReason;
            Logger.LogInformation("{N,-25} {I,-12} {C,-12} {S,-15} {A,-20} {R}",
                item.Name, item.InstalledVersion ?? "-", item.CatalogVersion ?? "-",
                item.ModelState ?? "Not Installed", action,
                reason);
        }

        // AB#6869: every blocker of every shown blocked row, so the operator sees the update order without Studio.
        var blocked = items.Where(i => i.IsBlocked && !i.IsServiceManaged && i.IsCompatible).ToList();
        if (blocked.Count == 0)
            return;

        Logger.LogInformation("");
        Logger.LogInformation("Would break:");
        foreach (var item in blocked)
        {
            Logger.LogInformation("{Name} {Installed} -> {Catalog}", item.Name, item.InstalledVersion ?? "-", item.CatalogVersion ?? "-");
            foreach (var line in UpgradeBlockerFormatter.DetailLines(item.Blockers))
                Logger.LogInformation("{Line}", line);
        }
    }
}
