using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using GraphQlDtos.DataTransferObjects.System.Communication.v4;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class DeployWorkloadCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly CommunicationMethodCall _methods;
    private readonly IArgument _workloadRtId;

    public DeployWorkloadCommand(ILogger<DeployWorkloadCommand> logger, IOptions<OctoToolOptions> options,
        ICommunicationServicesClient communicationServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "DeployWorkload",
            "Triggers a deploy of one workload (Adapter or Application) through its parent deployment site.",
            options, communicationServicesClient, authenticationService)
    {
        _methods = new CommunicationMethodCall(tenantClient, authenticationService, logger);
        _workloadRtId = CommandArgumentValue.AddArgument("id", "workloadRtId",
            ["The workload's runtime object ID"], true, 1);
    }

    public override async Task Execute()
    {
        var workloadRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_workloadRtId);

        Logger.LogInformation(
            "Deploying workload '{WorkloadRtId}' for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            workloadRtId, Options.Value.TenantId, ServiceClient.ServiceUri);

        var warning = await _methods.DeployWorkloadAsync(workloadRtId);
        if (!string.IsNullOrWhiteSpace(warning))
        {
            // Debug only: the output of this command is unchanged (deployment pipelines log it); the warning stays
            // readable on the workload (lastDeploymentWarning).
            Logger.LogDebug("Workload '{WorkloadRtId}' deploy warning: {Warning}", workloadRtId, warning);
        }

        Logger.LogInformation("Workload '{WorkloadRtId}' deploy triggered", workloadRtId);
    }
}
