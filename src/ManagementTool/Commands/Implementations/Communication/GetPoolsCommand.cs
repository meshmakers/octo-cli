using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

internal class GetPoolsCommand : ServiceClientOctoCommand<ICommunicationServicesClient>
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Converters = { new StringEnumConverter() }
    };

    private readonly IConsoleService _consoleService;
    private readonly IArgument _jsonArg;

    public GetPoolsCommand(ILogger<GetPoolsCommand> logger, IOptions<OctoToolOptions> options,
        IConsoleService consoleService,
        ICommunicationServicesClient communicationServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.CommunicationServicesGroup, "GetPools",
            "Gets all pools for the current tenant.", options,
            communicationServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _jsonArg = CommandArgumentValue.AddArgument("j", "json", ["Output as raw JSON"], false);
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Getting pools for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            Options.Value.TenantId,
            ServiceClient.ServiceUri);

        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        var result = await ServiceClient.GetPoolsAsync();

        if (CommandArgumentValue.IsArgumentUsed(_jsonArg))
        {
            _consoleService.WriteLine(JsonConvert.SerializeObject(result, JsonSettings));
        }
        else
        {
            _consoleService.WriteLine(JsonConvert.SerializeObject(result, Formatting.Indented, JsonSettings));
        }
    }
}
