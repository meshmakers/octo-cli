using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class ExecutePipelineCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _idArg;
    private readonly IArgument _inputFileArg;

    public ExecutePipelineCommand(ILogger<ExecutePipelineCommand> logger, IOptions<OctoToolOptions> options,
        IConsoleService consoleService,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "ExecutePipeline",
            "Executes a pipeline and returns the execution ID.", options,
            communicationServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _idArg = CommandArgumentValue.AddArgument("id", "identifier", ["The pipeline runtime object ID"], true, 1);
        _inputFileArg = CommandArgumentValue.AddArgument("f", "inputFile", ["Path to pipeline input file"], false, 1);
    }

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

        var executionId = await ServiceClient.ExecutePipelineAsync(id, input);

        _consoleService.WriteLine(executionId);
    }
}
