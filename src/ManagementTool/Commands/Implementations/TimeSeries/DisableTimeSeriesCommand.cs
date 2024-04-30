using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.TimeSeries;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.TimeSeries;

public class DisableTimeSeriesCommand : ServiceClientOctoCommand<ITimeSeriesServicesClient>
{
    public DisableTimeSeriesCommand(ILogger<DisableTimeSeriesCommand> logger,
        IOptions<OctoToolOptions> options, ITimeSeriesServicesClient serviceClient,
        IAuthenticationService authenticationService) : base(logger, "DisableTimeSeries",
        "Disable time series services for the current tenant.", options,
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

        
        Logger.LogInformation("Disable time series for tenant \'{TenantId}\' at \'{ServiceClientServiceUri}\'",
            Options.Value.TenantId,
            ServiceClient.ServiceUri);

        await ServiceClient.DisableAsync(Options.Value.TenantId);

        Logger.LogInformation("Time series for tenant \'{ClientId}\' at \'{ServiceClientServiceUri}\' disabled",
            Options.Value.TenantId,
            ServiceClient.ServiceUri);
    }
}