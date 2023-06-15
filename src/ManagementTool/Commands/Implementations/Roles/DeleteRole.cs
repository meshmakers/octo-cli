using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Roles;

internal class DeleteRole : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _nameArg;

    public DeleteRole(ILogger<DeleteRole> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "DeleteRole", "Deletes a role", options, identityServicesClient, authenticationService)
    {
        _nameArg = CommandArgumentValue.AddArgument("n", "name", new[] { "Name of role" }, true,
            1);
    }

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg).ToLower();

        Logger.LogInformation("Deleting role \'{Name}\' at \'{ServiceClientServiceUri}\'", name,
            ServiceClient.ServiceUri);

        await ServiceClient.DeleteRole(name);

        Logger.LogInformation("Role \'{Name}\' deleted", name);
    }
}
