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
///     destructive verbs. An applied repair is a <b>request</b> the server executes in the background (nothing runs
///     inside the call, so an ingress timeout cannot cut it); <c>--wait</c> polls the request until it is Done or Failed.
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
            ["After --apply: poll the repair request until it is Done or Failed and print the final report"],
            false, 0);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
        _jsonArg = CommandArgumentValue.AddArgument("j", "json", ["Output the report as raw JSON"], false, 0);
    }

    /// <summary>Time between two polls of the repair request; a property so tests do not wait for real seconds.</summary>
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
                "recomputes ranges. The server only accepts a repair request and executes it in the background; it keeps running " +
                "when this command ends. A second --apply while a repair is active returns the active request.",
                "--wait polls the request (up to 4 hours) and prints the final report; the command exits non-zero when the repair failed. " +
                "Without --wait, follow it with GetRollupRepairStatus -rid <requestId>.",
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

        if (!apply)
        {
            var plan = await ServiceClient.PlanRollupRepairAsync(tenantId, rollupRtIds);
            if (json)
            {
                _consoleService.WriteLine(JsonConvert.SerializeObject(plan, Formatting.Indented));
            }
            else
            {
                RollupConsistencyFormatter.Print(_consoleService, plan);
            }

            if (plan.Actions.Count > 0)
            {
                Logger.LogInformation("Nothing was changed. Run again with --apply to request these steps.");
            }

            return;
        }

        var request = await ServiceClient.EnqueueRollupRepairAsync(tenantId, rollupRtIds);
        Logger.LogInformation(
            "Rollup repair request '{RequestId}' is {State}; it runs in the background on the server. Follow it with " +
            "'GetRollupRepairStatus -rid {RequestId}'.", request.RequestId, request.State, request.RequestId);
        // With --wait --json only the final request is printed, so stdout stays one JSON document a script can parse.
        if (!(wait && json))
        {
            PrintRequest(request, json);
        }

        if (!wait)
        {
            return;
        }

        request = await WaitForRequestAsync(tenantId, request);
        Logger.LogInformation("Rollup repair request '{RequestId}' ended: {State}", request.RequestId, request.State);
        PrintRequest(request, json);
        RollupRepairRequestOutcome.ThrowIfFailed(request);
    }

    private void PrintRequest(RollupRepairRequestDto request, bool json)
    {
        if (json)
        {
            _consoleService.WriteLine(JsonConvert.SerializeObject(request, Formatting.Indented));
            return;
        }

        RollupConsistencyFormatter.Print(_consoleService, request);
    }

    private async Task<RollupRepairRequestDto> WaitForRequestAsync(string tenantId, RollupRepairRequestDto request)
    {
        var deadline = DateTime.UtcNow + PollTimeout;
        while (!request.IsFinished)
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new ToolException(
                    $"Rollup repair request '{request.RequestId}' is still {request.State} after {(int)PollTimeout.TotalHours} h. " +
                    $"It keeps running on the server; check it with GetRollupRepairStatus -rid {request.RequestId}.");
            }

            await Task.Delay(PollInterval);
            request = await ServiceClient.GetRollupRepairRequestAsync(tenantId, request.RequestId);
        }

        return request;
    }
}
