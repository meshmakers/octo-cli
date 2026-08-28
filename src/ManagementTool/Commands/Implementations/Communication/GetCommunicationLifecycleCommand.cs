using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class GetCommunicationLifecycleCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    public GetCommunicationLifecycleCommand(ILogger<GetCommunicationLifecycleCommand> logger,
        IOptions<OctoToolOptions> options,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "GetCommunicationLifecycle",
            "Gets the tenant's on-demand lifecycle configuration (scale-to-zero, AB#4914).", options,
            communicationServicesClient, authenticationService)
    {
    }

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        var lifecycle = await ServiceClient.GetLifecycleAsync();

        Logger.LogInformation(
            "Communication lifecycle for tenant '{TenantId}': ScaleToZeroEnabled={ScaleToZeroEnabled}",
            Options.Value.TenantId, lifecycle.ScaleToZeroEnabled);
    }
}
