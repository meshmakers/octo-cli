using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class GetPipelineExecutionsCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _idArg;
    private readonly IArgument _jsonArg;

    public GetPipelineExecutionsCommand(ILogger<GetPipelineExecutionsCommand> logger, IOptions<OctoToolOptions> options,
        IConsoleService consoleService,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "GetPipelineExecutions",
            "Returns pipeline execution history. For a failed execution, FailureKind tells whether a write was not acknowledged (OutcomeUnknown) or the pipeline failed (Failed).", options,
            communicationServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _idArg = CommandArgumentValue.AddArgument("id", "identifier", ["The pipeline runtime ID"], true, 1);
        _jsonArg = CommandArgumentValue.AddArgument("j", "json", ["Output as raw JSON"], false);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_idArg, "cc0000000000000000000003")], description: "Basic usage"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_idArg, "cc0000000000000000000003"),
                    new CodeSampleArgument(_jsonArg),
                ],
                    description: "Output as compact JSON"),
            ]
        );

    public override async Task Execute()
    {
        var id = CommandArgumentValue.GetArgumentScalarValue<string>(_idArg);

        Logger.LogInformation(
            "Getting pipeline executions '{PipelineId}' for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            id, Options.Value.TenantId, ServiceClient.ServiceUri);

        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        var result = await ServiceClient.GetPipelineExecutionsAsync(id);

        if (CommandArgumentValue.IsArgumentUsed(_jsonArg))
        {
            _consoleService.WriteLine(PipelineExecutionOutput.Serialize(result, false));
        }
        else
        {
            _consoleService.WriteLine(PipelineExecutionOutput.Serialize(result, true));
        }
    }
}
