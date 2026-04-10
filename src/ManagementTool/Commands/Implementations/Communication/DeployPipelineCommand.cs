using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class DeployPipelineCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly IArgument _adapterIdArg;
    private readonly IArgument _pipelineIdArg;
    private readonly IArgument _fileArg;

    public DeployPipelineCommand(ILogger<DeployPipelineCommand> logger, IOptions<OctoToolOptions> options,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "DeployPipeline",
            "Deploys a pipeline definition to the corresponding adapter.", options,
            communicationServicesClient, authenticationService)
    {
        _adapterIdArg = CommandArgumentValue.AddArgument("aid", "adapterId", ["The adapter runtime ID"], true, 1);
        _pipelineIdArg = CommandArgumentValue.AddArgument("pid", "pipelineId", ["The pipeline runtime ID"], true, 1);
        _fileArg = CommandArgumentValue.AddArgument("f", "file", ["Path to pipeline definition file (YAML/JSON)"], true, 1);
    }

    public override async Task Execute()
    {
        var adapterId = CommandArgumentValue.GetArgumentScalarValue<string>(_adapterIdArg);
        var pipelineId = CommandArgumentValue.GetArgumentScalarValue<string>(_pipelineIdArg);
        var filePath = CommandArgumentValue.GetArgumentScalarValue<string>(_fileArg);

        Logger.LogInformation(
            "Deploying pipeline '{PipelineId}' to adapter '{AdapterId}' for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            pipelineId, adapterId, Options.Value.TenantId, ServiceClient.ServiceUri);

        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        var pipelineDefinition = await File.ReadAllTextAsync(filePath);

        await ServiceClient.DeployPipelineAsync(adapterId, pipelineId, pipelineDefinition);

        Logger.LogInformation("Pipeline deployed successfully");
    }
}
