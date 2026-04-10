using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class DeployAdapterCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly IArgument _idArg;

    public DeployAdapterCommand(ILogger<DeployAdapterCommand> logger, IOptions<OctoToolOptions> options,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "DeployAdapter",
            "Deploys the adapter configuration update.", options,
            communicationServicesClient, authenticationService)
    {
        _idArg = CommandArgumentValue.AddArgument("id", "identifier", ["The adapter runtime ID"], true, 1);
    }

    public override async Task Execute()
    {
        var id = CommandArgumentValue.GetArgumentScalarValue<string>(_idArg);

        Logger.LogInformation(
            "Deploying adapter '{AdapterId}' for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            id, Options.Value.TenantId, ServiceClient.ServiceUri);

        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        await ServiceClient.DeployAdapterAsync(id);

        Logger.LogInformation(
            "Adapter '{AdapterId}' for tenant '{TenantId}' at '{ServiceClientServiceUri}' deployed successfully",
            id, Options.Value.TenantId, ServiceClient.ServiceUri);
    }
}
