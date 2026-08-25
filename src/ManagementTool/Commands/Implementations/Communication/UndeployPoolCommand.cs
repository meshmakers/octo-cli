using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class UndeployPoolCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _poolRtId;
    private readonly IArgument _yesArg;

    public UndeployPoolCommand(ILogger<UndeployPoolCommand> logger, IOptions<OctoToolOptions> options,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.CommunicationServicesGroup, "UndeployPool",
            "Undeploys a pool through the Communication Operator. Undeploy its workloads first (UndeployWorkload); Cloud pools release their operator resources.",
            options, communicationServicesClient, authenticationService)
    {
        _confirmationService = confirmationService;

        _poolRtId = CommandArgumentValue.AddArgument("id", "poolRtId",
            ["The pool's runtime object ID"], true, 1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_poolRtId, "670000000000000000000001")],
                    description: "Basic usage"),
                new CodeSample(arguments:
                    [
                        new CodeSampleArgument(_poolRtId, "670000000000000000000001"),
                        new CodeSampleArgument(_yesArg),
                    ],
                    description: "Non-interactive"),
            ],
            Notes:
            [
                "Undeploy the pool's workloads (Adapters and Applications) with UndeployWorkload before the pool itself; " +
                "the operator removes the pool resources (CommunicationPool resource and broker secret for Cloud pools) " +
                "once nothing runs in it.",
                "Required before DisableCommunication, which is refused with HTTP 409 while any pool or workload of the " +
                "tenant is still deployed (AB#4255).",
                "Acts on the tenant of the active context; switch with UseContext or pass --context <name>.",
            ]
        );

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        var poolRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_poolRtId);

        if (!CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm(
                $"Undeploy pool '{poolRtId}' of tenant '{Options.Value.TenantId}'? The operator will remove its resources."))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation(
            "Undeploying pool '{PoolRtId}' for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            poolRtId, Options.Value.TenantId, ServiceClient.ServiceUri);

        await ServiceClient.UndeployPoolAsync(poolRtId);

        Logger.LogInformation("Pool '{PoolRtId}' undeploy triggered", poolRtId);
    }
}
