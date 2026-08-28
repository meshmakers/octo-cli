using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class SetCommunicationLifecycleCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly IArgument _scaleToZeroEnabledArg;

    public SetCommunicationLifecycleCommand(ILogger<SetCommunicationLifecycleCommand> logger,
        IOptions<OctoToolOptions> options,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "SetCommunicationLifecycle",
            "Sets the tenant's on-demand lifecycle configuration (scale-to-zero, AB#4914). " +
            "Runtime configuration - effective without a controller redeploy; " +
            "'-sze false' is the per-tenant emergency stop.", options,
            communicationServicesClient, authenticationService)
    {
        _scaleToZeroEnabledArg = CommandArgumentValue.AddArgument("sze", "scaleToZeroEnabled",
            ["true to allow OnDemand workloads of this tenant to scale to 0 replicas when idle, false to disable (default)"],
            true, 1);
    }

    public override async Task Execute()
    {
        var scaleToZeroEnabled = CommandArgumentValue.GetArgumentScalarValue<bool>(_scaleToZeroEnabledArg);

        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        Logger.LogInformation(
            "Setting communication lifecycle for tenant '{TenantId}' at '{ServiceClientServiceUri}': ScaleToZeroEnabled={ScaleToZeroEnabled}",
            Options.Value.TenantId, ServiceClient.ServiceUri, scaleToZeroEnabled);

        var result = await ServiceClient.SetLifecycleAsync(new CommunicationLifecycleDto(scaleToZeroEnabled));

        Logger.LogInformation(
            "Communication lifecycle for tenant '{TenantId}' updated: ScaleToZeroEnabled={ScaleToZeroEnabled}",
            Options.Value.TenantId, result.ScaleToZeroEnabled);
    }
}
