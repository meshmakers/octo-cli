using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Users;

internal class AddUserToRole : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _nameArg;
    private readonly IArgument _roleArg;

    public AddUserToRole(ILogger<AddUserToRole> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "AddUserToRole", "Adds an user to a role", options,
            identityServicesClient, authenticationService)
    {
        _nameArg = CommandArgumentValue.AddArgument("un", "userName", ["User name"], true,
            1);
        _roleArg = CommandArgumentValue.AddArgument("r", "role", ["Existing role name"], false,
            1);
    }

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg).ToLower();
        var roleName = CommandArgumentValue.GetArgumentScalarValue<string>(_roleArg).ToLower();

        Logger.LogInformation("Adding user \'{Name}\' to role \'{Role}\' at \'{ServiceClientServiceUri}\'", name,
            roleName,
            ServiceClient.ServiceUri);

        if (!string.IsNullOrWhiteSpace(roleName))
            try
            {
                await ServiceClient.GetRoleByName(roleName);
            }
            catch (ServiceClientResultException)
            {
                Logger.LogError("Role \'{RoleName}\' does not exist at Service \'{ClientServiceUriUri}\'", roleName,
                    ServiceClient.ServiceUri);
                return;
            }

        await ServiceClient.AddRoleToUser(name, roleName);

        Logger.LogInformation("User \'{Name}\' updated", name);
    }
}