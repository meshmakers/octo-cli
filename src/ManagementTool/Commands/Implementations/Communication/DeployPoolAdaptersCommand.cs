using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class DeployPoolAdaptersCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly IArgument _poolIdArg;
    private readonly IArgument _adapterIdArg;

    public DeployPoolAdaptersCommand(ILogger<DeployPoolAdaptersCommand> logger, IOptions<OctoToolOptions> options,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "DeployPoolAdapters",
            "Deploys adapters for a specific pool.", options,
            communicationServicesClient, authenticationService)
    {
        _poolIdArg = CommandArgumentValue.AddArgument("pid", "poolId", ["The pool runtime ID"], true, 1);
        _adapterIdArg = CommandArgumentValue.AddArgument("aid", "adapterId",
            ["Deploy a specific adapter only"], false, 1);
    }

    public override async Task Execute()
    {
        var poolId = CommandArgumentValue.GetArgumentScalarValue<string>(_poolIdArg);

        Logger.LogInformation(
            "Deploying pool adapters for pool '{PoolId}' tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            poolId, Options.Value.TenantId, ServiceClient.ServiceUri);

        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        if (CommandArgumentValue.IsArgumentUsed(_adapterIdArg))
        {
            var adapterId = CommandArgumentValue.GetArgumentScalarValue<string>(_adapterIdArg);
            await ServiceClient.DeployPoolAdapterAsync(poolId, adapterId);
            Logger.LogInformation(
                "Adapter '{AdapterId}' deployed successfully for pool '{PoolId}' tenant '{TenantId}'",
                adapterId, poolId, Options.Value.TenantId);
        }
        else
        {
            await ServiceClient.DeployPoolAdaptersAsync(poolId);
            Logger.LogInformation("All adapters deployed successfully for pool '{PoolId}' tenant '{TenantId}'",
                poolId, Options.Value.TenantId);
        }
    }
}
