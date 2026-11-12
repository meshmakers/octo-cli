using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;

internal class ReviewTenantOwnership : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _stampArg;

    public ReviewTenantOwnership(ILogger<ReviewTenantOwnership> logger,
        IConsoleService consoleService,
        IOptions<OctoToolOptions> options, IAssetServicesClient assetServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "ReviewTenantOwnership",
            "Lists this instance's tenant databases without an ownership marker, with the marker of another " +
            "OctoMesh instance, or registered by another instance on the same MongoDB server and, with --stamp, " +
            "stamps the unambiguous ones. Lists only by default.", options,
            assetServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _stampArg = CommandArgumentValue.AddArgument("s", "stamp",
        [
            "Write this instance's ownership marker on the unambiguous databases (no marker, registered only here). " +
            "Without it the command only lists."
        ], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [], description: "List the tenant databases whose ownership is not unambiguous"),
                new CodeSample(arguments: [new CodeSampleArgument(_stampArg)],
                    description: "Stamp the unambiguous unmarked databases, list the rest"),
            ],
            Notes:
            [
                "Several OctoMesh instances can share one MongoDB server (test-2 main and dev instance, local " +
                "octo-devstack slots). The ownership marker in each tenant database names the instance that owns it " +
                "(AB#4945); databases from before the marker have none (AB#8796).",
                "--stamp never touches a database with another instance's marker or a database another instance " +
                "registers; they stay listed. A second --stamp stamps nothing new.",
                "A double registration (RegisteredByOtherInstance) is resolved by detaching the tenant " +
                "(DetachTenant) in the instance that must not have it — never by deleting it: delete drops the " +
                "shared database.",
                "Only answered on the system tenant (user tokens need AdminPanelManagement), otherwise HTTP 403. " +
                "Needs an asset-repo service that has the route (AB#8796); an older service answers 404.",
            ]
        );

    public override async Task Execute()
    {
        var stamp = CommandArgumentValue.IsArgumentUsed(_stampArg);
        Logger.LogInformation("Reviewing tenant-database ownership ({Mode}) on '{ServiceClientServiceUri}'",
            stamp ? "stamp" : "list", ServiceClient.ServiceUri);

        var report = await ServiceClient.ReviewTenantOwnershipAsync(stamp);

        foreach (var line in Format(report))
        {
            _consoleService.WriteLine(line);
        }
    }

    internal static IEnumerable<string> Format(TenantOwnershipReviewReportDto report)
    {
        yield return $"{(report.Stamp ? "Stamp" : "List")} on instance '{report.SystemDatabaseName}': checked " +
                     $"{report.Checked}, owned {report.Owned}, listed {report.Entries.Count}, " +
                     $"stamped {report.Entries.Count(e => e.Stamped)}, missing databases {report.MissingDatabases}";

        foreach (var entry in report.Entries)
        {
            var state = entry.Stamped ? "stamped" : entry.Issue;
            yield return $"  {state}: {entry.TenantId} (database {entry.DatabaseName}, marker " +
                         $"{entry.MarkerOwnerSystemDatabaseName ?? "none"}, registered in " +
                         $"{string.Join(", ", entry.RegisteringSystemDatabases)})";
        }

        if (!report.Stamp && report.Entries.Any(e => e.Issue == "Unmarked"))
        {
            yield return "Nothing was written. Run with --stamp to stamp the Unmarked databases listed above.";
        }
    }
}
