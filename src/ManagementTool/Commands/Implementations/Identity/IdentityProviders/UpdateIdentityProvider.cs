using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.IdentityProviders;

internal class UpdateIdentityProvider : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _clientId;
    private readonly IArgument _clientSecret;
    private readonly IArgument _enabled;
    private readonly IArgument _name;
    private readonly IArgument _rtId;

    public UpdateIdentityProvider(ILogger<UpdateIdentityProvider> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "UpdateIdentityProvider", "Updates an identity provider.",
            options, identityServicesClient,
            authenticationService)
    {
        _rtId = CommandArgumentValue.AddArgument("id", "identifier",
            ["ID of identity provider, must be unique"], true,
            1);
        _name = CommandArgumentValue.AddArgument("n", "name",
            ["Name of identity provider, must be unique"], true,
            1);
        _enabled = CommandArgumentValue.AddArgument("e", "enabled",
            ["True if identity provider should be enabled, otherwise false"], true,
            1);
        _clientId = CommandArgumentValue.AddArgument("cid", "clientId",
            ["ServiceClient ID, provided by provider"], true, 1);
        _clientSecret = CommandArgumentValue.AddArgument("cs", "clientSecret",
            ["ServiceClient secret, provided by provider"], true, 1);
    }

    public override async Task Execute()
    {
        var rtId = CommandArgumentValue.GetArgumentScalarValue<OctoObjectId>(_rtId);

        Logger.LogInformation("Updating identity provider \'{RtId}\' at \'{ServiceClientServiceUri}\'", rtId,
            ServiceClient.ServiceUri);

        var identityProviderDto = await ServiceClient.GetIdentityProvider(rtId);
        if (identityProviderDto == null)
        {
            Logger.LogError("Identity provider \'{RtId}\' at \'{ServiceClientServiceUri}\' not found", rtId,
                ServiceClient.ServiceUri);
            return;
        }

        var isEnabled = CommandArgumentValue.GetArgumentScalarValue<bool>(_enabled);
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientId);
        var clientSecret = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_clientSecret);
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_name);


        if (identityProviderDto is GoogleIdentityProviderDto)
        {
            var newIdentityProviderDto = new GoogleIdentityProviderDto
            {
                IsEnabled = isEnabled,
                ClientId = clientId,
                ClientSecret = clientSecret,
                Name = name
            };
            await ServiceClient.UpdateIdentityProvider(rtId, newIdentityProviderDto);
        }
        else if (identityProviderDto is MicrosoftIdentityProviderDto)
        {
            var newIdentityProviderDto = new MicrosoftIdentityProviderDto
            {
                IsEnabled = isEnabled,
                ClientId = clientId,
                ClientSecret = clientSecret,
                Name = name
            };
            await ServiceClient.UpdateIdentityProvider(rtId, newIdentityProviderDto);
        }
        else if (identityProviderDto is FacebookIdentityProviderDto)
        {
            var newIdentityProviderDto = new FacebookIdentityProviderDto
            {
                IsEnabled = isEnabled,
                ClientId = clientId,
                ClientSecret = clientSecret,
                Name = name
            };
            await ServiceClient.UpdateIdentityProvider(rtId, newIdentityProviderDto);
        }

        Logger.LogInformation("Identity provider \'{Id}\' at \'{ServiceClientServiceUri}\' updated", rtId,
            ServiceClient.ServiceUri);
    }
}