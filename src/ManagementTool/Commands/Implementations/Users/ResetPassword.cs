using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Users;

internal class ResetPassword : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _nameArg;
    private readonly IArgument _passwordArg;

    public ResetPassword(ILogger<ResetPassword> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "ResetPassword", "Resets the password of a user", options, identityServicesClient,
            authenticationService)
    {
        _nameArg = CommandArgumentValue.AddArgument("un", "userName", new[] { "User name" }, true,
            1);
        _passwordArg = CommandArgumentValue.AddArgument("p", "password", new[] { "New password of user" }, true,
            1);
    }

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg).ToLower();
        var password = CommandArgumentValue.GetArgumentScalarValue<string>(_passwordArg);

        Logger.LogInformation("Resetting password for user \'{Name}\' at \'{ServiceClientServiceUri}\'", name,
            ServiceClient.ServiceUri);

        await ServiceClient.ResetPassword(name, password);

        Logger.LogInformation("Resetting password for user \'{Name}\' done", name);
    }
}
