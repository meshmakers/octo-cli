using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

/// <summary>
///     Read-only consistency check of the rollup archives of the tenant (AB#6968): the check to run after a CrateDB
///     restore, before and after <c>RepairRollups</c>.
/// </summary>
internal class ValidateRollupsCommand : ServiceClientOctoCommand<IStreamDataServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _failOnFindingsArg;
    private readonly IArgument _jsonArg;
    private readonly IArgument _rollupRtIdsArg;

    public ValidateRollupsCommand(ILogger<ValidateRollupsCommand> logger, IOptions<OctoToolOptions> options,
        IConsoleService consoleService, IStreamDataServicesClient serviceClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "ValidateRollups",
            "Checks the rollup archives of the tenant for the inconsistencies a restore (or a dump taken during a recompute) can leave: " +
            "a pointer without rows, rows above the pointer, a watermark ahead of the rows, a missing generation map. " +
            "Read only: the server runs counts and MAX/MIN statements, nothing is changed.",
            options, serviceClient, authenticationService)
    {
        _consoleService = consoleService;
        _rollupRtIdsArg = CommandArgumentValue.AddArgument("id", "identifiers",
            ["Optional: comma-separated runtime ids of the rollup archives to check (default: all rollups of the tenant)"],
            false, 1);
        _failOnFindingsArg = CommandArgumentValue.AddArgument("ff", "failOnFindings",
            ["Exit with a non-zero code when any rollup is inconsistent (for scripts and the DR runbook)"], false, 0);
        _jsonArg = CommandArgumentValue.AddArgument("j", "json", ["Output the report as raw JSON"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [], description: "Check all rollup archives of the tenant"),
                new CodeSample(arguments: [new CodeSampleArgument(_rollupRtIdsArg, "69fda707d47638c68edc7fea")],
                    description: "Check one rollup archive"),
                new CodeSample(arguments: [new CodeSampleArgument(_failOnFindingsArg)],
                    description: "Check all rollups and exit non-zero when one is inconsistent (DR runbook gate after a restore)")
            ],
            Notes:
            [
                "Run it after a CrateDB restore. If it reports findings, run RepairRollups (dry run first).",
                "Read only and safe to repeat; the check uses counts and MAX/MIN statements and exports no rows.",
                "The tenant is the tenant of the active context."
            ]);

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            throw ToolException.NoTenantIdConfigured();
        }

        var tenantId = Options.Value.TenantId;
        var rollupRtIds = RollupIdList.Parse(CommandArgumentValue.IsArgumentUsed(_rollupRtIdsArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_rollupRtIdsArg)
            : null);

        Logger.LogInformation("Validating rollups of tenant '{TenantId}' at '{ServiceClientServiceUri}'", tenantId,
            ServiceClient.ServiceUri);

        var report = await ServiceClient.ValidateRollupsAsync(tenantId, rollupRtIds);

        if (CommandArgumentValue.IsArgumentUsed(_jsonArg))
        {
            _consoleService.WriteLine(JsonConvert.SerializeObject(report, Formatting.Indented));
        }
        else
        {
            RollupConsistencyFormatter.Print(_consoleService, report, $"Rollup validation of tenant '{tenantId}':");
        }

        if (!report.IsOk)
        {
            Logger.LogWarning(
                "Rollups are inconsistent. Run RepairRollups (dry run first) to see the planned repairs.");
            if (CommandArgumentValue.IsArgumentUsed(_failOnFindingsArg))
            {
                throw new ToolException(
                    $"{report.Entries.Count(e => !e.IsOk)} rollup(s) of tenant '{tenantId}' are inconsistent.");
            }
        }
    }
}
