using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts;
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
        _id = CommandArgumentValue.AddArgument("id", "identifier", ["ID of identity provider, must be unique"],
            true,
            1);
    }

    public override async Task Execute()
    {
        var rtId = CommandArgumentValue.GetArgumentScalarValue<OctoObjectId>(_id);


        Logger.LogInformation("Deleting identity provider \'{RtId}\' from \'{ServiceClientServiceUri}\'", rtId,
            ServiceClient.ServiceUri);

        await ServiceClient.DeleteIdentityProvider(rtId);

        Logger.LogInformation("Identity provider \'{RtId}\' deleted", rtId);
    }
}
