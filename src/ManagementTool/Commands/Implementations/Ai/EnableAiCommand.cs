using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AiServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Ai;

internal class EnableAiCommand : ServiceClientOctoCommand<IAiServicesClient>
{
    public EnableAiCommand(ILogger<EnableAiCommand> logger, IOptions<OctoToolOptions> options,
        IAiServicesClient aiServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.AiServicesGroup, "EnableAi",
            "Enables the AI Adapter for the current tenant. The Communication Controller must be enabled first (run EnableCommunication beforehand).",
            options, aiServicesClient, authenticationService)
    {
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Enabling AI Adapter for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            Options.Value.TenantId,
            ServiceClient.ServiceUri);

        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        await ServiceClient.EnableAsync(Options.Value.TenantId);

        Logger.LogInformation("AI Adapter for tenant '{TenantId}' at '{ServiceClientServiceUri}' enabled",
            Options.Value.TenantId,
            ServiceClient.ServiceUri);
    }
}
