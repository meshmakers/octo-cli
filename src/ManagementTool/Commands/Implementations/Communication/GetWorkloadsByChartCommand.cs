using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class GetWorkloadsByChartCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _chartName;

    public GetWorkloadsByChartCommand(ILogger<GetWorkloadsByChartCommand> logger,
        IOptions<OctoToolOptions> options, IConsoleService consoleService,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "GetWorkloadsByChart",
            "Lists every Adapter / Application in the active tenant whose ChartName matches.",
            options, communicationServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _chartName = CommandArgumentValue.AddArgument("cn", "chartName",
            ["Helm chart name to filter by, e.g. 'octo-mesh-adapter'."],
            true, 1);
    }

    public override async Task Execute()
    {
        var chartName = CommandArgumentValue.GetArgumentScalarValue<string>(_chartName);

        Logger.LogInformation(
            "Listing workloads with chart '{ChartName}' for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            chartName, Options.Value.TenantId, ServiceClient.ServiceUri);

        var workloads = await ServiceClient.GetWorkloadsByChartAsync(chartName);
        _consoleService.WriteLine(JsonConvert.SerializeObject(workloads, Formatting.Indented));
    }
}
