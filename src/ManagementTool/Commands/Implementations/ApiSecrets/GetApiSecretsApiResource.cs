using System.Linq;
using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.Client.System;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.ApiSecrets;

internal class GetApiSecretsApiResource : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _nameArg;

    public GetApiSecretsApiResource(ILogger<GetApiSecretsApiResource> logger, IOptions<OctoToolOptions> options,
        IConsoleService consoleService,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "GetApiSecretsApiResource", "Gets all secrets of an API resource.", options, identityServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        
        _nameArg = CommandArgumentValue.AddArgument("n", "name", new[] { "Name of API resource" },
            true,
            1);
    }

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg);

        Logger.LogInformation("Getting API secrets for API resource \'{Name}\' from \'{ServiceClientServiceUri}\'", name, ServiceClient.ServiceUri);

        var result = await ServiceClient.GetApiSecretsForApiResource(name);
        if (!result.Any())
        {
            Logger.LogInformation("No API secrets has been returned");
            return;
        }

        var resultString = JsonConvert.SerializeObject(result, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }
}
