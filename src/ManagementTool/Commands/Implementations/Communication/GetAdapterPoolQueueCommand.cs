using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

/// <summary>
///     AB#4924 §10 — the adapter pool queue in the CLI, the same view Refinery Studio and the MCP
///     server show, off the same endpoint.
/// </summary>
/// <remarks>
///     🔴 <b>Position is printed as "position N in its tenant, M tenant(s) ahead" and never as a
///     single rank.</b> The pool serves borrowing tenants round-robin, so one number cannot describe
///     the order work runs in — item 1 of the tenant whose turn is next runs before item 2 of the
///     tenant currently being served (concept §5 "Fairness").
/// </remarks>
internal class GetAdapterPoolQueueCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _idArg;
    private readonly IArgument _jsonArg;

    public GetAdapterPoolQueueCommand(ILogger<GetAdapterPoolQueueCommand> logger, IOptions<OctoToolOptions> options,
        IConsoleService consoleService,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "GetAdapterPoolQueue",
            "Shows what an adapter pool has queued and what it currently has leased out.", options,
            communicationServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _idArg = CommandArgumentValue.AddArgument("id", "adapterPoolRtId",
            ["The adapter pool's runtime object ID"], true, 1);
        _jsonArg = CommandArgumentValue.AddArgument("j", "json", ["Output as raw JSON"], false);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_idArg, "670000000000000000000001")],
                    description: "Basic usage"),
                new CodeSample(arguments:
                    [
                        new CodeSampleArgument(_idArg, "670000000000000000000001"),
                        new CodeSampleArgument(_jsonArg)
                    ],
                    description: "Output as compact JSON")
            ],
            Notes:
            [
                "The tenant of the active context is the LENDING tenant — the pool is its entity. Every entry names " +
                "the borrowing tenant whose work it is.",
                "There is no global queue position, deliberately: the pool serves borrowing tenants round-robin, so " +
                "the truthful answer is the position inside the item's own tenant plus the number of tenants that " +
                "take a turn first.",
                "Entries that already hold a lease are listed too — they are what the waiting ones wait behind, and " +
                "they are the only entries carrying a member id. Cancel them with the running-pipeline path, not " +
                "with CancelQueuedExecution.",
                "Manual (non-pooled) adapters have no queue at all; they execute immediately.",
                "An empty queue is an idle pool, not an error."
            ]
        );

    public override async Task Execute()
    {
        var adapterPoolRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_idArg);

        Logger.LogInformation(
            "Getting the queue of adapter pool '{AdapterPoolRtId}' for tenant '{TenantId}' at " +
            "'{ServiceClientServiceUri}'",
            adapterPoolRtId, Options.Value.TenantId, ServiceClient.ServiceUri);

        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        var entries = await ServiceClient.GetAdapterPoolQueueAsync(adapterPoolRtId);

        if (CommandArgumentValue.IsArgumentUsed(_jsonArg))
        {
            _consoleService.WriteLine(JsonConvert.SerializeObject(entries));
            return;
        }

        if (entries.Count == 0)
        {
            // An idle pool, not a failure — say so instead of printing an empty table the reader has
            // to interpret.
            _consoleService.WriteLine($"Adapter pool '{adapterPoolRtId}' has an empty queue.");
            return;
        }

        var leased = entries.Where(e => e.IsLeased).ToList();
        var waiting = entries.Where(e => !e.IsLeased).ToList();

        _consoleService.WriteLine(
            $"Adapter pool '{adapterPoolRtId}': {leased.Count} leased, {waiting.Count} waiting.");

        foreach (var entry in leased)
        {
            _consoleService.WriteLine(
                $"  LEASED  {entry.ExecutionId}  tenant={entry.BorrowerTenantId}  " +
                $"pipeline={Describe(entry)}  class={DescribeClass(entry.ExecutionClass)}  " +
                $"queuedAt={entry.QueuedAtUtc:O}  member={entry.LeasedOnMemberId}  " +
                $"leaseExpires={(entry.LeaseExpiresAtUtc is { } expiry ? expiry.ToString("O") : "n/a")}");
        }

        foreach (var entry in waiting)
        {
            _consoleService.WriteLine(
                $"  WAITING {entry.ExecutionId}  tenant={entry.BorrowerTenantId}  " +
                $"pipeline={Describe(entry)}  class={DescribeClass(entry.ExecutionClass)}  " +
                $"queuedAt={entry.QueuedAtUtc:O}  " +
                $"position={entry.PositionInTenant} in its tenant, " +
                $"{entry.TenantsAheadInRotation} tenant(s) ahead in the rotation");
        }

        if (leased.Count > 0)
        {
            _consoleService.WriteLine(
                "Leased entries cannot be cancelled with CancelQueuedExecution — stopping one means " +
                "interrupting a running pipeline, which is a different operation.");
        }
    }

    private static string Describe(AdapterPoolQueueEntryDto entry) =>
        entry.PipelineName ?? entry.PipelineRtId ?? "<unresolved>";

    /// <summary>
    ///     An unknown class prints its number rather than being forced into one of the two known
    ///     names — the wire value is an int precisely so a newer controller can add one.
    /// </summary>
    private static string DescribeClass(int executionClass) => executionClass switch
    {
        0 => "Interactive",
        1 => "Batch",
        _ => $"Unknown({executionClass})"
    };
}
