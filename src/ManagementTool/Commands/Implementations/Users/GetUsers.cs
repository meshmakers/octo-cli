using System.Linq;
using System.Threading.Tasks;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Users;

internal class GetUsers : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConsoleService _consoleService;

    public GetUsers(ILogger<GetUsers> logger,
        IConsoleService consoleService,
        IOptions<OctoToolOptions> options, IIdentityServicesClient identityServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, "GetUsers", "Gets users.", options, identityServicesClient, authenticationService)
    {
        _consoleService = consoleService;
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Getting users from \'{ServiceClientServiceUri}\'", ServiceClient.ServiceUri);

        var result = await ServiceClient.GetUsers();

        var users = result.ToArray();
        if (!users.Any())
        {
            Logger.LogInformation("No users has been returned");
            return;
        }

        var resultString = JsonConvert.SerializeObject(result, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }
}
