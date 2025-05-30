using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Roles;

internal class UpdateRole : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _nameArg;
    private readonly IArgument _newNameArg;

    public UpdateRole(ILogger<UpdateRole> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "UpdateRole", "Updates a role", options, identityServicesClient,
            authenticationService)
    {
        _nameArg = CommandArgumentValue.AddArgument("n", "name", ["Name of role"], true,
            1);
        _newNameArg = CommandArgumentValue.AddArgument("nn", "newRoleName",
            ["New name of role"], false,
            1);
    }

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg).ToLower();

        string? newRoleName = null;
        if (CommandArgumentValue.IsArgumentUsed(_newNameArg))
            newRoleName = CommandArgumentValue.GetArgumentScalarValue<string>(_newNameArg).ToLower();

        Logger.LogInformation("Updating role \'{Name}\' at \'{ServiceClientServiceUri}\'", name,
            ServiceClient.ServiceUri);

        var roleDto = new RoleDto
        {
            Name = newRoleName
        };

        await ServiceClient.UpdateRole(name, roleDto);

        Logger.LogInformation("Role \'{Name}\' updated", name);
    }
}