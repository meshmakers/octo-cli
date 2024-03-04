using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.TimeSeries;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.TimeSeries;

public class DisableTimeseriesCommand : ServiceClientOctoCommand<ITimeSeriesServicesClient>
{
    public DisableTimeseriesCommand(ILogger<ServiceClientOctoCommand<ITimeSeriesServicesClient>> logger,
        IOptions<OctoToolOptions> options, ITimeSeriesServicesClient serviceClient,
        IAuthenticationService authenticationService) : base(logger, "DisableTimeseries",
        "Disable timeseries services for the current tenant.", options,
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

        
        Logger.LogInformation("Disable timeseries for tenant \'{TenantId}\' at \'{ServiceClientServiceUri}\'",
            Options.Value.TenantId,
            ServiceClient.ServiceUri);

        await ServiceClient.DisableAsync(Options.Value.TenantId);

        Logger.LogInformation("Timeseries for tenant \'{ClientId}\' at \'{ServiceClientServiceUri}\' disabled",
            Options.Value.TenantId,
            ServiceClient.ServiceUri);
    }
}