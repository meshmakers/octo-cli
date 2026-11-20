using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using GraphQlDtos.DataTransferObjects.System.Communication.v4;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Meshmakers.Common.CommandLineParser;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class DeployTriggersCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly CommunicationMethodCall _methods;
    public DeployTriggersCommand(ILogger<DeployTriggersCommand> logger, IOptions<OctoToolOptions> options,
        ICommunicationServicesClient communicationServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "DeployTriggers",
            "Deploys all triggers for the current tenant.", options,
            communicationServicesClient, authenticationService)
    {
        _methods = new CommunicationMethodCall(tenantClient, authenticationService, logger);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [], description: "Activates all pipeline triggers for the current tenant"),
            ]
        );

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

        await _methods.RunAsync(RtPipelineTriggerMethodClient.DeployAllMethodId,
            () => new RtPipelineTriggerMethodClient(_methods.TenantClient).DeployAllAsync());

        Logger.LogInformation("Triggers deployed successfully for tenant '{TenantId}'", Options.Value.TenantId);
    }
}
