using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Users;

internal class ResetPassword : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _nameArg;
    private readonly IArgument _passwordArg;
    private readonly IArgument _yesArg;

    public ResetPassword(ILogger<ResetPassword> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.IdentityServicesGroup, "ResetPassword", "Resets the password of a user", options,
            identityServicesClient,
            authenticationService)
    {
        _confirmationService = confirmationService;

        _nameArg = CommandArgumentValue.AddArgument("un", "userName", ["User name"], true,
            1);
        _passwordArg = CommandArgumentValue.AddArgument("p", "password", ["New password of user"], true,
            1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
    }

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg).ToLower();
        var password = CommandArgumentValue.GetArgumentScalarValue<string>(_passwordArg);

        if (!CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm($"Are you sure you want to reset the password for user '{name}'?"))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation("Resetting password for user \'{Name}\' at \'{ServiceClientServiceUri}\'", name,
            ServiceClient.ServiceUri);

        await ServiceClient.ResetPassword(name, password);

        Logger.LogInformation("Resetting password for user \'{Name}\' done", name);
    }
}
