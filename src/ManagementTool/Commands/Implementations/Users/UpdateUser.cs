using System.Collections.Generic;
using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Common.Shared.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Users;

internal class UpdateUser : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _eMailArg;
    private readonly IArgument _nameArg;
    private readonly IArgument _newNameArg;
    private readonly IArgument _roleArg;

    public UpdateUser(ILogger<UpdateUser> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "UpdateUser", "Updates an user", options, identityServicesClient, authenticationService)
    {
        _eMailArg = CommandArgumentValue.AddArgument("e", "eMail", new[] { "E-Mail of user" },
            false, 1);
        _nameArg = CommandArgumentValue.AddArgument("un", "userName", new[] { "User name" }, true,
            1);
        _newNameArg = CommandArgumentValue.AddArgument("nun", "newUserName",
            new[] { "New user name, if the user name has to be changed" }, false,
            1);
        _roleArg = CommandArgumentValue.AddArgument("r", "role", new[] { "Role of user" }, false,
            1);
    }

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg).ToLower();

        string? newUserName = null;
        if (CommandArgumentValue.IsArgumentUsed(_newNameArg))
        {
            newUserName = CommandArgumentValue.GetArgumentScalarValue<string>(_newNameArg).ToLower();
        }

        string? roleName = null;
        if (CommandArgumentValue.IsArgumentUsed(_roleArg))
        {
            roleName = CommandArgumentValue.GetArgumentScalarValue<string>(_roleArg).ToLower();
        }

        string? eMail = null;
        if (CommandArgumentValue.IsArgumentUsed(_eMailArg))
        {
            eMail = CommandArgumentValue.GetArgumentScalarValue<string>(_eMailArg).ToLower();
        }

        Logger.LogInformation("Updating user \'{Name}\' at \'{ServiceClientServiceUri}\'", name,
            ServiceClient.ServiceUri);

        var userDto = new UserDto
        {
            Email = eMail,
            Name = newUserName,
            Roles = new List<RoleDto>()
        };

        if (!string.IsNullOrWhiteSpace(roleName))
        {
            RoleDto roleDto;
            try
            {
                roleDto = await ServiceClient.GetRoleByName(roleName);
            }
            catch (ServiceClientResultException)
            {
                Logger.LogError("Role \'{RoleName}\' does not exist at Service \'{ClientServiceUriUri}\'", roleName,
                    ServiceClient.ServiceUri);
                return;
            }

            userDto.Roles = new[] { roleDto };
        }

        await ServiceClient.UpdateUser(name, userDto);

        Logger.LogInformation("User \'{Name}\' updated", name);
    }
}
