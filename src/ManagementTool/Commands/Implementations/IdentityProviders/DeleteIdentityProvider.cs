using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.IdentityProviders;

internal class DeleteIdentityProvider : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _id;

    public DeleteIdentityProvider(ILogger<DeleteIdentityProvider> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "DeleteIdentityProvider", "Deletes an identity provider.", options, identityServicesClient,
            authenticationService)
    {
        _id = CommandArgumentValue.AddArgument("id", "identifier", new[] { "ID of identity provider, must be unique" },
            true,
            1);
    }

    public override async Task Execute()
    {
        var id = CommandArgumentValue.GetArgumentScalarValue<string>(_id);


        Logger.LogInformation("Deleting identity provider \'{Id}\' from \'{ServiceClientServiceUri}\'", id,
            ServiceClient.ServiceUri);

        await ServiceClient.DeleteIdentityProvider(id);

        Logger.LogInformation("Identity provider \'{Id}\' deleted", id);
    }
}
