using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class UpdateWorkloadChartVersionCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly IArgument _workloadRtId;
    private readonly IArgument _chartVersion;

    public UpdateWorkloadChartVersionCommand(ILogger<UpdateWorkloadChartVersionCommand> logger,
        IOptions<OctoToolOptions> options,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "UpdateWorkloadChartVersion",
            "Sets ChartVersion on a single workload. Does NOT trigger a deploy — call DeployWorkload afterwards if needed.",
            options, communicationServicesClient, authenticationService)
    {
        _workloadRtId = CommandArgumentValue.AddArgument("id", "workloadRtId",
            ["The workload's runtime object ID"], true, 1);
        _chartVersion = CommandArgumentValue.AddArgument("cv", "chartVersion",
            ["The new chart version (SemVer, e.g. '1.2.3' or '1.2.3-beta.1')"], true, 1);
    }

    public override async Task Execute()
    {
        var workloadRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_workloadRtId);
        var chartVersion = CommandArgumentValue.GetArgumentScalarValue<string>(_chartVersion);

        Logger.LogInformation(
            "Setting chart version of workload '{WorkloadRtId}' to '{ChartVersion}' for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            workloadRtId, chartVersion, Options.Value.TenantId, ServiceClient.ServiceUri);

        await ServiceClient.UpdateWorkloadChartVersionAsync(workloadRtId, chartVersion);

        Logger.LogInformation(
            "Chart version for workload '{WorkloadRtId}' updated to '{ChartVersion}'. Deploy is NOT triggered — run DeployWorkload separately if needed.",
            workloadRtId, chartVersion);
    }
}
