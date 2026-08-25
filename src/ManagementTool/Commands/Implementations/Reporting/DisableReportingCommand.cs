using Meshmakers.Common.CommandLineParser;
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

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [], description: "Basic usage"),
            ],
            Notes:
            [
                "Disabling only removes the enabled flag: report definitions, resources and stored reports stay in " +
                "the tenant and are accessible again after EnableReporting. Until then the reporting API, the report " +
                "designer and the viewer answer HTTP 403 for this tenant.",
                "Disabling Reporting is a precondition for Delete and Detach of the tenant (AB#4255). It has no " +
                "precondition of its own - Reporting owns no deployed resources. Like the other disable commands it " +
                "acts on the tenant of the active context (UseContext or --context <name>).",
                "Reversible with EnableReporting.",
            ]
        );

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