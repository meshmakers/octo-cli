using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class GetPipelineDebugPointsCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _idArg;
    private readonly IArgument _executionIdArg;
    private readonly IArgument _jsonArg;

    public GetPipelineDebugPointsCommand(ILogger<GetPipelineDebugPointsCommand> logger,
        IOptions<OctoToolOptions> options,
        IConsoleService consoleService,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "GetPipelineDebugPoints",
            "Returns debug point nodes for a specific pipeline execution.", options,
            communicationServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _idArg = CommandArgumentValue.AddArgument("id", "identifier", ["The pipeline runtime ID"], true, 1);
        _executionIdArg = CommandArgumentValue.AddArgument("eid", "executionId", ["The execution ID (GUID)"], true, 1);
        _jsonArg = CommandArgumentValue.AddArgument("j", "json", ["Output as raw JSON"], false);
    }

    public override async Task Execute()
    {
        var id = CommandArgumentValue.GetArgumentScalarValue<string>(_idArg);
        var executionIdStr = CommandArgumentValue.GetArgumentScalarValue<string>(_executionIdArg);

        Logger.LogInformation(
            "Getting pipeline debug points '{PipelineId}' execution '{ExecutionId}' for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            id, executionIdStr, Options.Value.TenantId, ServiceClient.ServiceUri);

        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        var execId = Guid.Parse(executionIdStr);

        var rawJson = await ServiceClient.GetPipelineExecutionDebugPointsAsync(id, execId);

        if (CommandArgumentValue.IsArgumentUsed(_jsonArg))
        {
            _consoleService.WriteLine(rawJson);
        }
        else
        {
            var parsed = JsonConvert.DeserializeObject(rawJson);
            var formatted = JsonConvert.SerializeObject(parsed, Formatting.Indented);
            _consoleService.WriteLine(formatted);
        }
    }
}
