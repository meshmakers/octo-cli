using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class UndeployWorkloadCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _workloadRtId;
    private readonly IArgument _yesArg;

    public UndeployWorkloadCommand(ILogger<UndeployWorkloadCommand> logger, IOptions<OctoToolOptions> options,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.CommunicationServicesGroup, "UndeployWorkload",
            "Undeploys one workload (Adapter or Application) through its parent pool. Destructive — the operator helm-uninstalls the chart.",
            options, communicationServicesClient, authenticationService)
    {
        _confirmationService = confirmationService;

        _workloadRtId = CommandArgumentValue.AddArgument("id", "workloadRtId",
            ["The workload's runtime object ID"], true, 1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
    }

    public override async Task Execute()
    {
        var workloadRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_workloadRtId);

        if (!CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm(
                $"Undeploy workload '{workloadRtId}'? The operator will helm-uninstall its release."))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation(
            "Undeploying workload '{WorkloadRtId}' for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            workloadRtId, Options.Value.TenantId, ServiceClient.ServiceUri);

        await ServiceClient.UndeployWorkloadAsync(workloadRtId);

        Logger.LogInformation("Workload '{WorkloadRtId}' undeploy triggered", workloadRtId);
    }
}
