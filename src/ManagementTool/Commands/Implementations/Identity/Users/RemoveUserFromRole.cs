using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Users;

internal class RemoveUserFromRole : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _nameArg;
    private readonly IArgument _roleArg;

    public RemoveUserFromRole(ILogger<RemoveUserFromRole> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "RemoveUserFromRole", "Remove an user from a role", options,
            identityServicesClient, authenticationService)
    {
        _nameArg = CommandArgumentValue.AddArgument("un", "userName", ["User name"], true,
            1);
        _roleArg = CommandArgumentValue.AddArgument("r", "role", ["Role name"], false,
            1);
    }

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg).ToLower();
        var roleName = CommandArgumentValue.GetArgumentScalarValue<string>(_roleArg).ToLower();

        Logger.LogInformation("Removing user \'{Name}\' from role \'{Role}\' at \'{ServiceClientServiceUri}\'", name,
            roleName,
            ServiceClient.ServiceUri);

        await ServiceClient.RemoveRoleFromUser(name, roleName);

        Logger.LogInformation("User \'{Name}\' updated", name);
    }
}