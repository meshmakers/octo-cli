using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.IdentityProviders;

internal class AddOAuthIdentityProvider : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _clientId;
    private readonly IArgument _clientSecret;
    private readonly IArgument _enabled;
    private readonly IArgument _name;
    private readonly IArgument _type;

    public AddOAuthIdentityProvider(ILogger<AddOAuthIdentityProvider> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "AddOAuthIdentityProvider", "Adds a new identity provider.",
            options, identityServicesClient,
            authenticationService)
    {
        _name = CommandArgumentValue.AddArgument("n", "name", ["Name of identity provider, must be unique"],
            true,
            1);
        _enabled = CommandArgumentValue.AddArgument("e", "enabled",
            ["True if identity provider should be enabled, otherwise false"], true,
            1);
        _clientId = CommandArgumentValue.AddArgument("cid", "clientId",
            ["ServiceClient ID, provided by provider"], true, 1);
        _clientSecret = CommandArgumentValue.AddArgument("cs", "clientSecret",
            ["ServiceClient secret, provided by provider"], true, 1);
        _type = CommandArgumentValue.AddArgument("t", "type",
            ["Type of provider, available are 'google', 'microsoft', 'facebook'"], true, 1);
    }

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_name);
        var type = CommandArgumentValue.GetArgumentScalarValue<IdentityProviderTypesDto>(_type);
        var isEnabled = CommandArgumentValue.GetArgumentScalarValue<bool>(_enabled);
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientId);
        var clientSecret = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_clientSecret);

        Logger.LogInformation("Creating OAuth identity provider \'{Name}\' at \'{ServiceClientServiceUri}\'", name,
            ServiceClient.ServiceUri);

        if (type == IdentityProviderTypesDto.Google)
        {
            var identityProviderDto = new GoogleIdentityProviderDto
            {
                IsEnabled = isEnabled,
                ClientId = clientId,
                ClientSecret = clientSecret,
                Name = name
            };
            await ServiceClient.CreateIdentityProvider(identityProviderDto);
        }
        else if (type == IdentityProviderTypesDto.Microsoft)
        {
            var identityProviderDto = new MicrosoftIdentityProviderDto
            {
                IsEnabled = isEnabled,
                ClientId = clientId,
                ClientSecret = clientSecret,
                Name = name
            };
            await ServiceClient.CreateIdentityProvider(identityProviderDto);
        }
        else if (type == IdentityProviderTypesDto.Facebook)
        {
            var identityProviderDto = new FacebookIdentityProviderDto
            {
                IsEnabled = isEnabled,
                ClientId = clientId,
                ClientSecret = clientSecret,
                Name = name
            };
            await ServiceClient.CreateIdentityProvider(identityProviderDto);
        }

        Logger.LogInformation("Identity provider \'{Name}\' at \'{ServiceClientServiceUri}\' created", name,
            ServiceClient.ServiceUri);
    }
}