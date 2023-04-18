using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Common.Shared.DataTransferObjects;
using Meshmakers.Octo.Frontend.Client;
using Meshmakers.Octo.Frontend.Client.System;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Users;

internal class CreateUser : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _eMailArg;
    private readonly IArgument _nameArg;
    private readonly IArgument _roleArg;
    private readonly IArgument _passwordArg;

    public CreateUser(ILogger<CreateUser> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "CreateUser", "Create a new user account", options, identityServicesClient,
            authenticationService)
    {
        _eMailArg = CommandArgumentValue.AddArgument("e", "eMail", new[] { "E-Mail of user" },
            true, 1);
        _nameArg = CommandArgumentValue.AddArgument("un", "userName", new[] { "User name" }, true,
            1);
        _roleArg = CommandArgumentValue.AddArgument("r", "role", new[] { "Role of user" }, true,
            1);
        _passwordArg = CommandArgumentValue.AddArgument("p", "password", new[] { "Password" }, false,
            0);
    }


    public override async Task Execute()
    {
        var eMail = CommandArgumentValue.GetArgumentScalarValue<string>(_eMailArg).ToLower();
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg).ToLower();
        var roleName = CommandArgumentValue.GetArgumentScalarValue<string>(_roleArg).ToLower();
        var password = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_passwordArg);

        Logger.LogInformation("Creating user \'{Name}\' at \'{ServiceClientServiceUri}\'", name,
            ServiceClient.ServiceUri);

        RoleDto roleDto;
        try
        {
            roleDto = await ServiceClient.GetRoleByName(roleName);
        }
        catch (ServiceClientResultException)
        {
            Logger.LogError("Role \'{RoleName}\' does not exist at \'{ServiceClientServiceUri}\'", roleName,
                ServiceClient.ServiceUri);
            return;
        }


        var userDto = new RegisterUserDto()
        {
            Email = eMail,
            Name = name,
            Roles = new[] { roleDto },
            Password = password
        };

        await ServiceClient.CreateUser(userDto);

        Logger.LogInformation("User \'{Name}\' added", name);
    }
}
