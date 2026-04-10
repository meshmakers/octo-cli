using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class DeployDataFlowCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly IArgument _idArg;

    public DeployDataFlowCommand(ILogger<DeployDataFlowCommand> logger, IOptions<OctoToolOptions> options,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "DeployDataFlow",
            "Deploys a specific data flow.", options,
            communicationServicesClient, authenticationService)
    {
        _idArg = CommandArgumentValue.AddArgument("id", "identifier", ["The data flow runtime ID"], true, 1);
    }

    public override async Task Execute()
    {
        var id = CommandArgumentValue.GetArgumentScalarValue<string>(_idArg);

        Logger.LogInformation(
            "Deploying data flow '{DataFlowId}' for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            id, Options.Value.TenantId, ServiceClient.ServiceUri);

        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        await ServiceClient.DeployDataFlowAsync(id);

        Logger.LogInformation("Data flow '{DataFlowId}' deployed successfully for tenant '{TenantId}'",
            id, Options.Value.TenantId);
    }
}
