using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class GetAdapterNodesCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _jsonArg;

    public GetAdapterNodesCommand(ILogger<GetAdapterNodesCommand> logger, IOptions<OctoToolOptions> options,
        IConsoleService consoleService,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "GetAdapterNodes",
            "Gets aggregated node descriptors from all connected adapters.", options,
            communicationServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _jsonArg = CommandArgumentValue.AddArgument("j", "json", ["Output as raw JSON"], false);
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Getting adapter nodes for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            Options.Value.TenantId,
            ServiceClient.ServiceUri);

        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        var rawJson = await ServiceClient.GetAdapterNodesAsync();

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
