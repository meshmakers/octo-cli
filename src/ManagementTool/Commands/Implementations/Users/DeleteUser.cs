using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Users;

internal class DeleteUser : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _nameArg;

    public DeleteUser(ILogger<DeleteUser> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "DeleteUser", "Deletes an user", options, identityServicesClient, authenticationService)
    {
        _nameArg = CommandArgumentValue.AddArgument("un", "userName", ["User name"], true,
            1);
    }

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg).ToLower();

        Logger.LogInformation("Deleting user \'{Name}\' at \'{ServiceClientServiceUri}\'", name,
            ServiceClient.ServiceUri);

        await ServiceClient.DeleteUser(name);

        Logger.LogInformation("User \'{Name}\' deleted", name);
    }
}
