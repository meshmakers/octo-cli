using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Users;

internal class RemoveUserFromRole : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _nameArg;
    private readonly IArgument _roleArg;
    private readonly IArgument _yesArg;

    public RemoveUserFromRole(ILogger<RemoveUserFromRole> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.IdentityServicesGroup, "RemoveUserFromRole", "Remove an user from a role", options,
            identityServicesClient, authenticationService)
    {
        _confirmationService = confirmationService;

        _nameArg = CommandArgumentValue.AddArgument("un", "userName", ["User name"], true,
            1);
        _roleArg = CommandArgumentValue.AddArgument("r", "role", ["Role name"], false,
            1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
    }

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg).ToLower();
        var roleName = CommandArgumentValue.GetArgumentScalarValue<string>(_roleArg).ToLower();

        if (!CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm(
                $"Are you sure you want to remove user '{name}' from role '{roleName}'?"))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation("Removing user \'{Name}\' from role \'{Role}\' at \'{ServiceClientServiceUri}\'", name,
            roleName,
            ServiceClient.ServiceUri);

        await ServiceClient.RemoveRoleFromUser(name, roleName);

        Logger.LogInformation("User \'{Name}\' updated", name);
    }
}
