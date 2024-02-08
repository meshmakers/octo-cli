using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class EnableCommunicationCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    public EnableCommunicationCommand(ILogger<EnableCommunicationCommand> logger, IOptions<OctoToolOptions> options,
        ICommunicationServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "EnableCommunication", "Enables the communication controller for the current tenant.", options,
            identityServicesClient, authenticationService)
    {
        
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Enabling communication for tenant \'{TenantId}\' at \'{ServiceClientServiceUri}\'", Options.Value.TenantId,
            ServiceClient.ServiceUri);
        
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        await ServiceClient.EnableAsync(Options.Value.TenantId);

        Logger.LogInformation("Communication for tenant \'{ClientId}\' at \'{ServiceClientServiceUri}\' enabled", Options.Value.TenantId,
            ServiceClient.ServiceUri);
    }
}
