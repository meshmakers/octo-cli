using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.ReportingServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Reporting;

internal class DisableReportingCommand : ServiceClientOctoCommand<IReportingServicesClient>
{
    public DisableReportingCommand(ILogger<DisableReportingCommand> logger, IOptions<OctoToolOptions> options,
        IReportingServicesClient reportingServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.ReportingServicesGroup, "DisableReporting",
            "Disables reporting services for the current tenant.", options,
            reportingServicesClient, authenticationService)
    {
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Disable reporting for tenant \'{TenantId}\' at \'{ServiceClientServiceUri}\'",
            Options.Value.TenantId,
            ServiceClient.ServiceUri);

        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        await ServiceClient.DisableAsync(Options.Value.TenantId);

        Logger.LogInformation("Reporting for tenant \'{ClientId}\' at \'{ServiceClientServiceUri}\' disabled",
            Options.Value.TenantId,
            ServiceClient.ServiceUri);
    }
}