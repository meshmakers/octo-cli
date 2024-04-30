using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.TimeSeries;

public class EnableStreamDataCommand : ServiceClientOctoCommand<IStreamDataServicesClient>
{
    public EnableStreamDataCommand(ILogger<EnableStreamDataCommand> logger,
        IOptions<OctoToolOptions> options, IStreamDataServicesClient serviceClient,
        IAuthenticationService authenticationService) : base(logger, "EnableStreamData",
        "Enable stream data services for the current tenant.", options,
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

        
        Logger.LogInformation("Enable stream data for tenant \'{TenantId}\' at \'{ServiceClientServiceUri}\'",
            Options.Value.TenantId,
            ServiceClient.ServiceUri);

        await ServiceClient.EnableAsync(Options.Value.TenantId);

        Logger.LogInformation("Stream data for tenant \'{ClientId}\' at \'{ServiceClientServiceUri}\' enabled",
            Options.Value.TenantId,
            ServiceClient.ServiceUri);
    }
}