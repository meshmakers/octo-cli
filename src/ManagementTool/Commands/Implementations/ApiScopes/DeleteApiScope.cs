using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.Client.System;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.ApiScopes;

internal class DeleteApiScope : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _name;

    public DeleteApiScope(ILogger<DeleteApiScope> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "DeleteApiScope", "Deletes a client.", options, identityServicesClient, authenticationService)
    {
        _name = CommandArgumentValue.AddArgument("n", "name", new[] { "Name of scope, must be unique" },
            true,
            1);
    }

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_name);

        Logger.LogInformation("Deleting API scope \'{Name}\' from \'{ServiceClientServiceUri}\'", name,
            ServiceClient.ServiceUri);

        await ServiceClient.DeleteScope(name);

        Logger.LogInformation("API scope \'{Name}\' deleted", name);
    }
}
