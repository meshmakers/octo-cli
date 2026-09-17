using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

/// <summary>
///     AB#4924 §10 — cancels one entry that is still <b>waiting</b> in an adapter pool's queue.
/// </summary>
/// <remarks>
///     🔴 <b>This is not "stop that pipeline".</b> An execution that already holds a lease is refused
///     with its own message instead of being interrupted: cancelling a queued entry and interrupting
///     a running pipeline are two different operations with different consequences, and a verb that
///     silently did whichever applied would leave the operator unsure which one just happened
///     (concept §5, "Cancellation").
/// </remarks>
internal class CancelQueuedExecutionCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _executionIdArg;
    private readonly IArgument _idArg;
    private readonly IArgument _yesArg;

    public CancelQueuedExecutionCommand(ILogger<CancelQueuedExecutionCommand> logger,
        IOptions<OctoToolOptions> options,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.CommunicationServicesGroup, "CancelQueuedExecution",
            "Cancels one execution that is still waiting in an adapter pool's queue. It never runs.", options,
            communicationServicesClient, authenticationService)
    {
        _confirmationService = confirmationService;

        _idArg = CommandArgumentValue.AddArgument("id", "adapterPoolRtId",
            ["The adapter pool's runtime object ID"], true, 1);
        _executionIdArg = CommandArgumentValue.AddArgument("eid", "executionId",
            ["The queued execution to cancel"], true, 1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments:
                    [
                        new CodeSampleArgument(_idArg, "670000000000000000000001"),
                        new CodeSampleArgument(_executionIdArg, "8f1c0b9a-0001-4a8f-9d21-2f0c9c4f5a11")
                    ],
                    description: "Basic usage"),
                new CodeSample(arguments:
                    [
                        new CodeSampleArgument(_idArg, "670000000000000000000001"),
                        new CodeSampleArgument(_executionIdArg, "8f1c0b9a-0001-4a8f-9d21-2f0c9c4f5a11"),
                        new CodeSampleArgument(_yesArg)
                    ],
                    description: "Non-interactive")
            ],
            Notes:
            [
                "Cancels a QUEUE entry, not a running pipeline. An execution that already holds a lease is refused " +
                "(HTTP 409) — interrupting it is a different operation with different consequences.",
                "The cancelled execution becomes Cancelled and is never leased; it stays visible in the execution " +
                "history as a cancelled attempt.",
                "The tenant of the active context is the LENDING tenant that owns the adapter pool, not the borrowing tenant " +
                "whose work is cancelled. Find execution ids with GetAdapterPoolQueue.",
                "Acts on the tenant of the active context; switch with UseContext or pass --context <name>."
            ]
        );

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        var adapterPoolRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_idArg);
        var executionId = CommandArgumentValue.GetArgumentScalarValue<string>(_executionIdArg);

        if (!CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm(
                $"Cancel queued execution '{executionId}' of adapter pool '{adapterPoolRtId}'? It will never run. " +
                "This cancels a waiting queue entry only; an execution that already holds a lease is refused, " +
                "because interrupting a running pipeline is a different operation."))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation(
            "Cancelling queued execution '{ExecutionId}' of adapter pool '{AdapterPoolRtId}' for tenant " +
            "'{TenantId}' at '{ServiceClientServiceUri}'",
            executionId, adapterPoolRtId, Options.Value.TenantId, ServiceClient.ServiceUri);

        var result = await ServiceClient.CancelQueuedExecutionAsync(adapterPoolRtId, executionId);

        switch (result.Outcome)
        {
            case AdapterPoolQueueCancellationOutcome.Cancelled:
                Logger.LogInformation("Queued execution '{ExecutionId}' was cancelled and will never run",
                    executionId);
                break;

            case AdapterPoolQueueCancellationOutcome.AlreadyLeased:
                // 🔴 Its own outcome, not a generic failure: the operator has to learn that the work
                // is already running and that stopping it is the other operation.
                Logger.LogWarning(
                    "Execution '{ExecutionId}' already holds a lease and is no longer queued — nothing was " +
                    "cancelled. Stopping it means interrupting the running pipeline, which is a different " +
                    "operation. Server said: {ServerMessage}",
                    executionId, result.ServerMessage ?? "<no message>");
                break;

            default:
                Logger.LogWarning(
                    "No queued execution '{ExecutionId}' belongs to adapter pool '{AdapterPoolRtId}' — it never " +
                    "was queued here, or it already finished, failed or was cancelled. Server said: " +
                    "{ServerMessage}",
                    executionId, adapterPoolRtId, result.ServerMessage ?? "<no message>");
                break;
        }
    }
}
