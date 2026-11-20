using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using GraphQlDtos.DataTransferObjects.System.Communication.v4;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class ExecutePipelineCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly CommunicationMethodCall _methods;
    private readonly IConsoleService _consoleService;
    private readonly IArgument _idArg;
    private readonly IArgument _inputFileArg;

    public ExecutePipelineCommand(ILogger<ExecutePipelineCommand> logger, IOptions<OctoToolOptions> options,
        IConsoleService consoleService,
        ICommunicationServicesClient communicationServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "ExecutePipeline",
            "Executes a pipeline and returns the execution ID.", options,
            communicationServicesClient, authenticationService)
    {
        _methods = new CommunicationMethodCall(tenantClient, authenticationService, logger);
        _consoleService = consoleService;
        _idArg = CommandArgumentValue.AddArgument("id", "identifier", ["The pipeline runtime object ID"], true, 1);
        _inputFileArg = CommandArgumentValue.AddArgument("f", "inputFile", ["Path to pipeline input file"], false, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_idArg, "cc0000000000000000000003")], description: "Triggers a pipeline execution and returns the execution ID"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_idArg, "cc0000000000000000000003"),
                    new CodeSampleArgument(_inputFileArg, "./input.json"),
                ],
                    description: "Optionally provide input data from a file"),
            ]
        );

    public override async Task Execute()
    {
        var id = CommandArgumentValue.GetArgumentScalarValue<string>(_idArg);

        Logger.LogInformation(
            "Executing pipeline '{PipelineId}' for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            id, Options.Value.TenantId, ServiceClient.ServiceUri);

        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        string? input = null;
        if (CommandArgumentValue.IsArgumentUsed(_inputFileArg))
        {
            var inputFilePath = CommandArgumentValue.GetArgumentScalarValue<string>(_inputFileArg);
            input = await File.ReadAllTextAsync(inputFilePath);
        }

        CommunicationMethodCall.RequireObjectId(id, "PipelineRtId");
        var started = await _methods.RunWithResultAsync(RtPipelineMethodClient.ExecuteMethodId,
            () => new RtPipelineMethodClient(_methods.TenantClient).ExecuteAsync(id, input, isDryRun: false));
        var executionId = string.IsNullOrWhiteSpace(started.ExecutionId)
            ? throw new ServiceClientException(
                $"The CK method {RtPipelineMethodClient.ExecuteMethodId} returned no execution id.")
            : started.ExecutionId;

        _consoleService.WriteLine(executionId);
    }
}
