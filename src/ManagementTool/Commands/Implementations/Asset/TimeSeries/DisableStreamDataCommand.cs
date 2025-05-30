using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

public class DisableStreamDataCommand : ServiceClientOctoCommand<IStreamDataServicesClient>
{
    public DisableStreamDataCommand(ILogger<DisableStreamDataCommand> logger,
        IOptions<OctoToolOptions> options, IStreamDataServicesClient serviceClient,
        IAuthenticationService authenticationService) : base(logger, Constants.AssetRepositoryServicesGroup,
        "DisableStreamData",
        "Disable stream data services for the current tenant.", options,
        serviceClient, authenticationService)
    {
    }

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }


        Logger.LogInformation("Disable stream data for tenant \'{TenantId}\' at \'{ServiceClientServiceUri}\'",
            Options.Value.TenantId,
            ServiceClient.ServiceUri);

        await ServiceClient.DisableAsync(Options.Value.TenantId);

        Logger.LogInformation("Stream data for tenant \'{ClientId}\' at \'{ServiceClientServiceUri}\' disabled",
            Options.Value.TenantId,
            ServiceClient.ServiceUri);
    }
}