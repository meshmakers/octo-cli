using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using GraphQlDtos.DataTransferObjects.System.Communication.v4;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class UndeployWorkloadCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly CommunicationMethodCall _methods;
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _workloadRtId;
    private readonly IArgument _yesArg;

    public UndeployWorkloadCommand(ILogger<UndeployWorkloadCommand> logger, IOptions<OctoToolOptions> options,
        ICommunicationServicesClient communicationServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.CommunicationServicesGroup, "UndeployWorkload",
            "Undeploys one workload (Adapter or Application) through its parent deployment site. Destructive — the operator helm-uninstalls the chart.",
            options, communicationServicesClient, authenticationService)
    {
        _methods = new CommunicationMethodCall(tenantClient, authenticationService, logger);
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

        await _methods.UndeployWorkloadAsync(workloadRtId);

        Logger.LogInformation("Workload '{WorkloadRtId}' undeploy triggered", workloadRtId);
    }
}
