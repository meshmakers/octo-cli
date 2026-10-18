using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

/// <summary>
///     Repairs the rollup inconsistencies <c>ValidateRollups</c> finds (AB#6968). Idempotent. A dry run is the default
///     and lists the planned steps per rollup; <c>--apply</c> writes, so it is gated on the confirmation like the other
///     destructive verbs. The recompute ranges of an applied repair run as recompute jobs on the server; <c>--wait</c>
///     polls them and validates again.
/// </summary>
internal class RepairRollupsCommand : ServiceClientOctoCommand<IStreamDataServicesClient>
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromHours(4);

    private readonly IArgument _applyArg;
    private readonly IConfirmationService _confirmationService;
    private readonly IConsoleService _consoleService;
    private readonly IArgument _jsonArg;
    private readonly IArgument _rollupRtIdsArg;
    private readonly IArgument _waitArg;
    private readonly IArgument _yesArg;

    public RepairRollupsCommand(ILogger<RepairRollupsCommand> logger, IOptions<OctoToolOptions> options,
        IConsoleService consoleService, IStreamDataServicesClient serviceClient,
        IAuthenticationService authenticationService, IConfirmationService confirmationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "RepairRollups",
            "Repairs the rollup inconsistencies that ValidateRollups finds after a restore (rows above the pointer, a pointer without rows, " +
            "a watermark ahead of the rows, a missing generation map). Idempotent: a second run on a repaired tenant finds nothing to do. " +
            "Dry run by default: lists the planned steps per rollup and changes nothing; --apply runs them.",
            options, serviceClient, authenticationService)
    {
        _consoleService = consoleService;
        _confirmationService = confirmationService;
        _rollupRtIdsArg = CommandArgumentValue.AddArgument("id", "identifiers",
            ["Optional: comma-separated runtime ids of the rollup archives to repair (default: all rollups of the tenant)"],
            false, 1);
        _applyArg = CommandArgumentValue.AddArgument("a", "apply",
            ["Run the repair (default is a dry run that changes nothing)"], false, 0);
        _waitArg = CommandArgumentValue.AddArgument("w", "wait",
            ["After --apply: poll the recompute jobs until they finish, then validate again and print the result"],
            false, 0);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
        _jsonArg = CommandArgumentValue.AddArgument("j", "json", ["Output the report as raw JSON"], false, 0);
    }

    /// <summary>Time between two polls of the recompute jobs; a property so tests do not wait for real seconds.</summary>
    internal TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [], description: "Dry run: list the planned repairs per rollup, change nothing"),
                new CodeSample(
                    arguments: [new CodeSampleArgument(_applyArg), new CodeSampleArgument(_waitArg)],
                    description: "Repair all rollups, wait for the recompute jobs and validate again"),
                new CodeSample(
                    arguments:
                    [
                        new CodeSampleArgument(_rollupRtIdsArg, "69fda707d47638c68edc7fea"),
                        new CodeSampleArgument(_applyArg), new CodeSampleArgument(_yesArg)
                    ],
                    description: "Repair one rollup without the confirmation prompt (automation)")
            ],
            Notes:
            [
                "DR runbook after a CrateDB restore: ValidateRollups, then RepairRollups (dry run), then RepairRollups --apply --wait.",
                "Without --apply nothing is written and no confirmation is asked.",
                "--apply deletes rows above the generation the pointer selects, rewinds watermarks, creates missing generation maps and " +
                "recomputes ranges. The fast steps run at once; the recompute ranges are enqueued as recompute jobs " +
                "(see ListRecomputeJobs) and keep running on the server when this command ends.",
                "--wait polls those jobs (up to 4 hours) and validates again; the command exits non-zero when a step failed, a job failed or the tenant does not validate ok.",
                "Idempotent: repeating the command is safe.",
                "The tenant is the tenant of the active context."
            ]);

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            throw ToolException.NoTenantIdConfigured();
        }

        var tenantId = Options.Value.TenantId;
        var apply = CommandArgumentValue.IsArgumentUsed(_applyArg);
        var wait = CommandArgumentValue.IsArgumentUsed(_waitArg);
        var json = CommandArgumentValue.IsArgumentUsed(_jsonArg);
        var rollupRtIds = RollupIdList.Parse(CommandArgumentValue.IsArgumentUsed(_rollupRtIdsArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_rollupRtIdsArg)
            : null);

        if (wait && !apply)
        {
            Logger.LogWarning("--wait has no effect without --apply (a dry run starts no jobs).");
        }

        if (apply && !CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm(
                $"repair the rollups of tenant '{tenantId}'? Rows above the pointer are deleted, watermarks are rewound and ranges are recomputed"))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation("{Mode} the rollup repair for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            apply ? "Applying" : "Dry-running", tenantId, ServiceClient.ServiceUri);

        var report = await ServiceClient.RepairRollupsAsync(tenantId, rollupRtIds, !apply);

        if (json)
        {
            _consoleService.WriteLine(JsonConvert.SerializeObject(report, Formatting.Indented));
        }
        else
        {
            RollupConsistencyFormatter.Print(_consoleService, report);
        }

        ThrowOnFailedStep(report);

        if (!apply)
        {
            if (report.Actions.Count > 0)
            {
                Logger.LogInformation("Nothing was changed. Run again with --apply to execute these steps.");
            }

            return;
        }

        if (report.JobIds.Count == 0)
        {
            await ReportFinalStateAsync(tenantId, rollupRtIds, report.After);
            return;
        }

        Logger.LogInformation(
            "{Count} recompute job(s) enqueued: {JobIds}. They run in the background; follow them with 'ListRecomputeJobs -id <rollupRtId>'.",
            report.JobIds.Count, string.Join(", ", report.JobIds));
        if (!wait)
        {
            Logger.LogInformation("Run ValidateRollups when the jobs have finished.");
            return;
        }

        await WaitForJobsAsync(tenantId, report);
        await ReportFinalStateAsync(tenantId, rollupRtIds, null);
    }

    private void ThrowOnFailedStep(RollupRepairReportDto report)
    {
        if (!string.IsNullOrEmpty(report.FailedStep))
        {
            throw new ToolException($"The rollup repair failed at step: {report.FailedStep}");
        }
    }

    private async Task WaitForJobsAsync(string tenantId, RollupRepairReportDto report)
    {
        var jobs = report.Actions.Where(a => a.JobId is not null)
            .Select(a => (a.RollupRtId, JobId: a.JobId!)).Distinct().ToList();
        var failed = new List<string>();

        foreach (var (rollupRtId, jobId) in jobs)
        {
            Logger.LogInformation("Waiting for recompute job {JobId} of rollup '{RollupRtId}'...", jobId, rollupRtId);
            var (result, job) = await BackfillRollupCommand.PollUntilTerminalAsync(
                () => ServiceClient.ListRecomputeJobsForArchiveAsync(tenantId, rollupRtId),
                jobId, () => DateTime.UtcNow, Task.Delay, PollInterval, PollTimeout);

            switch (result)
            {
                case BackfillRollupCommand.BackfillPollResult.Terminal when job is not null:
                    Logger.LogInformation("Recompute job {JobId}: state={State}, rows={Rows}, error={Error}", job.RtId,
                        job.State, job.RowsProcessed?.ToString() ?? "n/a", job.ErrorReason ?? "none");
                    if (job.State.Equals("Failed", StringComparison.OrdinalIgnoreCase))
                    {
                        failed.Add($"{jobId} ({job.ErrorReason ?? "no reason reported"})");
                    }

                    break;
                case BackfillRollupCommand.BackfillPollResult.Vanished:
                    Logger.LogWarning("Recompute job {JobId} is no longer listed for rollup '{RollupRtId}'.", jobId,
                        rollupRtId);
                    break;
                default:
                    failed.Add($"{jobId} (still running after {(int)PollTimeout.TotalHours} h)");
                    break;
            }
        }

        if (failed.Count > 0)
        {
            throw new ToolException($"{failed.Count} recompute job(s) did not finish: {string.Join("; ", failed)}");
        }
    }

    private async Task ReportFinalStateAsync(string tenantId, IReadOnlyCollection<string>? rollupRtIds,
        RollupConsistencyReportDto? after)
    {
        after ??= await ServiceClient.ValidateRollupsAsync(tenantId, rollupRtIds);
        RollupConsistencyFormatter.Print(_consoleService, after, $"Rollup validation of tenant '{tenantId}' after the repair:");
        if (!after.IsOk)
        {
            throw new ToolException(
                $"{after.Entries.Count(e => !e.IsOk)} rollup(s) are still inconsistent after the repair.");
        }
    }
}
