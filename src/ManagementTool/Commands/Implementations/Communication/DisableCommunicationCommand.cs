using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class DisableCommunicationCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    public DisableCommunicationCommand(ILogger<DisableCommunicationCommand> logger, IOptions<OctoToolOptions> options,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "DisableCommunication",
            "Disables the communication controller for the current tenant.", options,
            communicationServicesClient, authenticationService)
    {
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [], description: "Basic usage"),
            ],
            Notes:
            [
                "Refused with HTTP 409 while pools or workloads (Adapters and Applications) of the tenant are still " +
                "deployed; the error names them with their deployment state. Undeploy them first with UndeployWorkload " +
                "and UndeployPool - like this command they act on the tenant of the active context (UseContext or " +
                "--context <name>).",
                "Also refused with HTTP 409 while AI Services is still enabled for the tenant (AB#4884) - the AI " +
                "service depends on Communication. Disable it first with DisableAi.",
                "Disabling Communication is a precondition for Delete and Detach of the tenant (AB#4255); the disable " +
                "itself removes the trigger schedules and unloads the tenant from the controller, it does not undeploy " +
                "anything.",
                "Reversible with EnableCommunication.",
            ]
        );

    public override async Task Execute()
    {
        Logger.LogInformation("Disable communication for tenant \'{TenantId}\' at \'{ServiceClientServiceUri}\'",
            Options.Value.TenantId,
            ServiceClient.ServiceUri);

        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        await ServiceClient.DisableAsync(Options.Value.TenantId);

        Logger.LogInformation("Communication for tenant \'{ClientId}\' at \'{ServiceClientServiceUri}\' disabled",
            Options.Value.TenantId,
            ServiceClient.ServiceUri);
    }
}