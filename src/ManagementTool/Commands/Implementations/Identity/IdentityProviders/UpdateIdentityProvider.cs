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
    private readonly IArgument _allowSelfRegistration;
    private readonly IArgument _clientId;
    private readonly IArgument _clientSecret;
    private readonly IArgument _defaultGroupRtId;
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
            ["ServiceClient ID, provided by provider"], false, 1);
        _clientSecret = CommandArgumentValue.AddArgument("cs", "clientSecret",
            ["ServiceClient secret, provided by provider"], false, 1);
        _allowSelfRegistration = CommandArgumentValue.AddArgument("asr", "allowSelfRegistration",
            ["Allow self registration (default: true)"], false, 1);
        _defaultGroupRtId = CommandArgumentValue.AddArgument("dgid", "defaultGroupRtId",
            ["Default group RtId for new users"], false, 1);
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
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_name);
        var clientId = CommandArgumentValue.IsArgumentUsed(_clientId)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_clientId)
            : null;
        var clientSecret = CommandArgumentValue.IsArgumentUsed(_clientSecret)
            ? CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_clientSecret)
            : null;

        IdentityProviderDto newIdentityProviderDto;

        if (identityProviderDto is GoogleIdentityProviderDto)
        {
            newIdentityProviderDto = new GoogleIdentityProviderDto
            {
                IsEnabled = isEnabled,
                ClientId = clientId,
                ClientSecret = clientSecret,
                Name = name
            };
        }
        else if (identityProviderDto is MicrosoftIdentityProviderDto)
        {
            newIdentityProviderDto = new MicrosoftIdentityProviderDto
            {
                IsEnabled = isEnabled,
                ClientId = clientId,
                ClientSecret = clientSecret,
                Name = name
            };
        }
        else if (identityProviderDto is FacebookIdentityProviderDto)
        {
            newIdentityProviderDto = new FacebookIdentityProviderDto
            {
                IsEnabled = isEnabled,
                ClientId = clientId,
                ClientSecret = clientSecret,
                Name = name
            };
        }
        else if (identityProviderDto is AzureEntraIdProviderDto existingAzure)
        {
            newIdentityProviderDto = new AzureEntraIdProviderDto
            {
                IsEnabled = isEnabled,
                ClientId = clientId ?? existingAzure.ClientId,
                ClientSecret = clientSecret ?? existingAzure.ClientSecret,
                TenantId = existingAzure.TenantId,
                Authority = existingAzure.Authority,
                Name = name
            };
        }
        else if (identityProviderDto is MicrosoftAdProviderDto existingAd)
        {
            newIdentityProviderDto = new MicrosoftAdProviderDto
            {
                IsEnabled = isEnabled,
                Host = existingAd.Host,
                Port = existingAd.Port,
                Name = name
            };
        }
        else if (identityProviderDto is OpenLdapProviderDto existingLdap)
        {
            newIdentityProviderDto = new OpenLdapProviderDto
            {
                IsEnabled = isEnabled,
                Host = existingLdap.Host,
                Port = existingLdap.Port,
                UserBaseDn = existingLdap.UserBaseDn,
                UserNameAttribute = existingLdap.UserNameAttribute,
                Name = name
            };
        }
        else if (identityProviderDto is OctoTenantIdentityProviderDto existingOctoTenant)
        {
            newIdentityProviderDto = new OctoTenantIdentityProviderDto
            {
                IsEnabled = isEnabled,
                ParentTenantId = existingOctoTenant.ParentTenantId,
                Name = name
            };
        }
        else
        {
            Logger.LogError("Unsupported identity provider type for '{RtId}'", rtId);
            return;
        }

        if (CommandArgumentValue.IsArgumentUsed(_allowSelfRegistration))
        {
            newIdentityProviderDto.AllowSelfRegistration =
                CommandArgumentValue.GetArgumentScalarValue<bool>(_allowSelfRegistration);
        }

        if (CommandArgumentValue.IsArgumentUsed(_defaultGroupRtId))
        {
            newIdentityProviderDto.DefaultGroupRtId =
                CommandArgumentValue.GetArgumentScalarValue<string>(_defaultGroupRtId);
        }

        await ServiceClient.UpdateIdentityProvider(rtId, newIdentityProviderDto);

        Logger.LogInformation("Identity provider \'{Id}\' at \'{ServiceClientServiceUri}\' updated", rtId,
            ServiceClient.ServiceUri);
    }
}
