using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;

internal class ReconcileTenantRegistry : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _applyArg;

    public ReconcileTenantRegistry(ILogger<ReconcileTenantRegistry> logger,
        IConsoleService consoleService,
        IOptions<OctoToolOptions> options, IAssetServicesClient assetServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "ReconcileTenantRegistry",
            "Finds tenants that a parent lists but the system registry lacks (GetTenants -r vs -a) and, with --apply, " +
            "inserts the missing registry rows. Dry run by default.", options,
            assetServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _applyArg = CommandArgumentValue.AddArgument("a", "apply",
        [
            "Write the missing system registry rows. Without it the command only reports (dry run)."
        ], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [], description: "Dry run: report what would be inserted and any conflicts"),
                new CodeSample(arguments: [new CodeSampleArgument(_applyArg)],
                    description: "Insert the missing system registry rows"),
            ],
            Notes:
            [
                "A tenant that its parent lists (GetTenants -r) but that has no row in the system registry " +
                "(GetTenants -a) is invisible to client mirror provisioning, token cleanup, CORS and health checks " +
                "(AB#6941). Rows are only inserted, never changed or deleted; rows that disagree with the parent's " +
                "registry and registries that cannot be read are reported as conflicts and left alone. A second " +
                "--apply inserts nothing.",
                "Only answered on the system tenant (user tokens need AdminPanelManagement), otherwise HTTP 403. " +
                "Needs an asset-repo service that has the route (AB#6971); an older service answers 404.",
                "Runbook: run without --apply, review the inserts and conflicts, run with --apply, then re-run the " +
                "client mirror backfill (ProvisionClientInExistingTenants).",
            ]
        );

    public override async Task Execute()
    {
        var apply = CommandArgumentValue.IsArgumentUsed(_applyArg);
        Logger.LogInformation("Reconciling the tenant registry ({Mode}) on '{ServiceClientServiceUri}'",
            apply ? "apply" : "dry run", ServiceClient.ServiceUri);

        var report = await ServiceClient.ReconcileTenantRegistryAsync(apply);

        foreach (var line in Format(report))
        {
            _consoleService.WriteLine(line);
        }
    }

    internal static IEnumerable<string> Format(TenantRegistryReconcileReportDto report)
    {
        var verb = report.Applied ? "inserted" : "to insert";
        yield return $"{(report.Applied ? "Applied" : "Dry run")}: walked {report.Walked}, {verb} " +
                     $"{report.Inserted.Count}, already present {report.AlreadyPresent}, " +
                     $"conflicts {report.Conflicts.Count}";

        foreach (var insert in report.Inserted)
        {
            yield return $"  {verb}: {insert.TenantId} (database {insert.DatabaseName}, parent {insert.ParentTenantId})";
        }

        foreach (var conflict in report.Conflicts)
        {
            yield return $"  conflict: {conflict.TenantId} (parent {conflict.ParentTenantId}): {conflict.Reason}";
        }

        if (!report.Applied && report.Inserted.Count > 0)
        {
            yield return "Nothing was written. Run with --apply to insert the rows listed above.";
        }
    }
}
