using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class DeployTriggersCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    public DeployTriggersCommand(ILogger<DeployTriggersCommand> logger, IOptions<OctoToolOptions> options,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "DeployTriggers",
            "Deploys all triggers for the current tenant.", options,
            communicationServicesClient, authenticationService)
    {
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Deploying triggers for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            Options.Value.TenantId,
            ServiceClient.ServiceUri);

        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        await ServiceClient.DeployTriggersAsync();

        Logger.LogInformation("Triggers deployed successfully for tenant '{TenantId}'", Options.Value.TenantId);
    }
}
