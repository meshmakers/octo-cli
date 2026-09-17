using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class DeployDeploymentSiteCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly IArgument _deploymentSiteRtId;

    public DeployDeploymentSiteCommand(ILogger<DeployDeploymentSiteCommand> logger, IOptions<OctoToolOptions> options,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "DeployDeploymentSite",
            "Triggers a deploy of a deployment site. The Communication Operator creates the deployment site resources; workloads are deployed separately via DeployWorkload.",
            options, communicationServicesClient, authenticationService)
    {
        _deploymentSiteRtId = CommandArgumentValue.AddArgument("id", "deploymentSiteRtId",
            ["The deployment site's runtime object ID"], true, 1);
    }

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        var deploymentSiteRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_deploymentSiteRtId);

        Logger.LogInformation(
            "Deploying deployment site '{PoolRtId}' for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            deploymentSiteRtId, Options.Value.TenantId, ServiceClient.ServiceUri);

        await ServiceClient.DeployDeploymentSiteAsync(deploymentSiteRtId);

        Logger.LogInformation("Deployment site '{PoolRtId}' deploy triggered", deploymentSiteRtId);
    }
}
