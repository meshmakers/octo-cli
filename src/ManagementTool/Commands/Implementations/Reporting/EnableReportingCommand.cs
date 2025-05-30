using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.ReportingServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Reporting;

internal class EnableReportingCommand : ServiceClientOctoCommand<IReportingServicesClient>
{
    public EnableReportingCommand(ILogger<EnableReportingCommand> logger, IOptions<OctoToolOptions> options,
        IReportingServicesClient reportingServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.ReportingServicesGroup, "EnableReporting",
            "Enables reporting services for the current tenant.", options,
            reportingServicesClient, authenticationService)
    {
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Enabling reporting for tenant \'{TenantId}\' at \'{ServiceClientServiceUri}\'",
            Options.Value.TenantId,
            ServiceClient.ServiceUri);

        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        await ServiceClient.EnableAsync(Options.Value.TenantId);

        Logger.LogInformation("Reporting for tenant \'{ClientId}\' at \'{ServiceClientServiceUri}\' enabled",
            Options.Value.TenantId,
            ServiceClient.ServiceUri);
    }
}