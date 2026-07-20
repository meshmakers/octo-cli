using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class DeployPoolCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly IArgument _poolRtId;

    public DeployPoolCommand(ILogger<DeployPoolCommand> logger, IOptions<OctoToolOptions> options,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "DeployPool",
            "Triggers a deploy of a pool. The Communication Operator creates the pool resources; workloads are deployed separately via DeployWorkload.",
            options, communicationServicesClient, authenticationService)
    {
        _poolRtId = CommandArgumentValue.AddArgument("id", "poolRtId",
            ["The pool's runtime object ID"], true, 1);
    }

    public override async Task Execute()
    {
        var poolRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_poolRtId);

        Logger.LogInformation(
            "Deploying pool '{PoolRtId}' for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            poolRtId, Options.Value.TenantId, ServiceClient.ServiceUri);

        await ServiceClient.DeployPoolAsync(poolRtId);

        Logger.LogInformation("Pool '{PoolRtId}' deploy triggered", poolRtId);
    }
}
