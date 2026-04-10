using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class GetPipelineSchemaCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _adapterIdArg;
    private readonly IArgument _outputFileArg;

    public GetPipelineSchemaCommand(ILogger<GetPipelineSchemaCommand> logger, IOptions<OctoToolOptions> options,
        IConsoleService consoleService,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "GetPipelineSchema",
            "Gets the pipeline JSON schema for a specific adapter.", options,
            communicationServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _adapterIdArg =
            CommandArgumentValue.AddArgument("aid", "adapterId", ["The adapter runtime ID"], true, 1);
        _outputFileArg =
            CommandArgumentValue.AddArgument("o", "outputFile", ["Output file path"], false, 1);
    }

    public override async Task Execute()
    {
        var adapterId = CommandArgumentValue.GetArgumentScalarValue<string>(_adapterIdArg);

        Logger.LogInformation(
            "Getting pipeline schema for adapter '{AdapterId}' for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            adapterId, Options.Value.TenantId, ServiceClient.ServiceUri);

        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        var rawJson = await ServiceClient.GetPipelineSchemaAsync(adapterId);

        if (CommandArgumentValue.IsArgumentUsed(_outputFileArg))
        {
            var outputFile = CommandArgumentValue.GetArgumentScalarValue<string>(_outputFileArg);
            await File.WriteAllTextAsync(outputFile, rawJson);
            Logger.LogInformation("Pipeline schema written to '{OutputFile}'", outputFile);
        }
        else
        {
            var parsed = JsonConvert.DeserializeObject(rawJson);
            var formatted = JsonConvert.SerializeObject(parsed, Formatting.Indented);
            _consoleService.WriteLine(formatted);
        }
    }
}
