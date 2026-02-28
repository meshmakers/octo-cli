using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.IdentityProviders;

internal class DeleteIdentityProvider : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _id;
    private readonly IArgument _yesArg;

    public DeleteIdentityProvider(ILogger<DeleteIdentityProvider> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.IdentityServicesGroup, "DeleteIdentityProvider", "Deletes an identity provider.",
            options, identityServicesClient,
            authenticationService)
    {
        _confirmationService = confirmationService;

        _id = CommandArgumentValue.AddArgument("id", "identifier", ["ID of identity provider, must be unique"],
            true,
            1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
    }

    public override async Task Execute()
    {
        var rtId = CommandArgumentValue.GetArgumentScalarValue<OctoObjectId>(_id);

        if (!CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm($"Are you sure you want to delete identity provider '{rtId}'?"))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation("Deleting identity provider \'{RtId}\' from \'{ServiceClientServiceUri}\'", rtId,
            ServiceClient.ServiceUri);

        await ServiceClient.DeleteIdentityProvider(rtId);

        Logger.LogInformation("Identity provider \'{RtId}\' deleted", rtId);
    }
}
