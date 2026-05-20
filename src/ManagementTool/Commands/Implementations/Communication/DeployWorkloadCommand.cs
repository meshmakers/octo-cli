using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class DeployWorkloadCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly IArgument _workloadRtId;

    public DeployWorkloadCommand(ILogger<DeployWorkloadCommand> logger, IOptions<OctoToolOptions> options,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "DeployWorkload",
            "Triggers a deploy of one workload (Adapter or Application) through its parent pool.",
            options, communicationServicesClient, authenticationService)
    {
        _workloadRtId = CommandArgumentValue.AddArgument("id", "workloadRtId",
            ["The workload's runtime object ID"], true, 1);
    }

    public override async Task Execute()
    {
        var workloadRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_workloadRtId);

        Logger.LogInformation(
            "Deploying workload '{WorkloadRtId}' for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            workloadRtId, Options.Value.TenantId, ServiceClient.ServiceUri);

        await ServiceClient.DeployWorkloadAsync(workloadRtId);

        Logger.LogInformation("Workload '{WorkloadRtId}' deploy triggered", workloadRtId);
    }
}
