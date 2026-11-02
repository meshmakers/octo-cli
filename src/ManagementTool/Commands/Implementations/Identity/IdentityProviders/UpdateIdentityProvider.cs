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
    private readonly IArgument _clientSecretStdin;
    private readonly SecretInput _secretInput;
    private readonly IArgument _defaultGroupRtId;
    private readonly IArgument _enabled;
    private readonly IArgument _name;
    private readonly IArgument _rtId;

    public UpdateIdentityProvider(ILogger<UpdateIdentityProvider> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService,
        SecretInput secretInput)
        : base(logger, Constants.IdentityServicesGroup, "UpdateIdentityProvider", "Updates an identity provider.",
            options, identityServicesClient,
            authenticationService)
    {
        _secretInput = secretInput;
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
            [
                "Deprecated: new client secret, provided by the provider; omit every secret source to keep the stored secret. A value on the command line ends up in the shell history and the process list.",
                $"Use --client-secret-stdin or the environment variable {Constants.EnvVarIdentityProviderClientSecret} instead."
            ], false, 1);
        _clientSecretStdin = CommandArgumentValue.AddArgument("css", "client-secret-stdin",
            ["Read the client secret from standard input (one value; a trailing line break is removed)"], false, 0);
        _allowSelfRegistration = CommandArgumentValue.AddArgument("asr", "allowSelfRegistration",
            ["Allow self registration (default: true)"], false, 1);
        _defaultGroupRtId = CommandArgumentValue.AddArgument("dgid", "defaultGroupRtId",
            ["Default group RtId for new users"], false, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_rtId, "provider-id"),
                    new CodeSampleArgument(_name, "Updated Name"),
                    new CodeSampleArgument(_enabled, "true"),
                    new CodeSampleArgument(_allowSelfRegistration, "false"),
                    new CodeSampleArgument(_defaultGroupRtId, "<default-group-rtid>"),
                ],
                    description: "Works for all provider types. Fetches the existing provider, preserves type-specific properties, and applies the changes"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_rtId, "provider-id"),
                    new CodeSampleArgument(_name, "Updated Name"),
                    new CodeSampleArgument(_enabled, "true"),
                    new CodeSampleArgument(_clientId, "new-client-id"),
                    new CodeSampleArgument(_clientSecretStdin),
                ],
                    description: "For OAuth-based providers, you can also update client credentials (the new secret is piped in on standard input)"),
            ],
            Notes:
            [
                "The client secret is write-only: GetIdentityProviders shows clientSecretIsSet, never the value.",
                $"A new client secret rotates the stored one; it is read from standard input (--client-secret-stdin) or from the environment variable {Constants.EnvVarIdentityProviderClientSecret}. Without either the stored secret is kept (there is no prompt). It cannot be cleared. -cs still works in this release but logs a deprecation warning, because a value on the command line ends up in the shell history and the process list.",
            ]
        );

    public override async Task Execute()
    {
        var rtId = OctoObjectId.Parse(CommandArgumentValue.GetArgumentScalarValue<string>(_rtId));

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
        // AB#5543: the client secret is write-only. GET never returns it (clientSecretIsSet instead), and PUT
        // treats null / "" as "keep the stored secret" — so only a newly supplied value is sent, never the
        // existing DTO's ClientSecret.
        var clientSecret = _secretInput.Resolve(CommandArgumentValue,
            new SecretSpec("client secret", _clientSecret, _clientSecretStdin,
                Constants.EnvVarIdentityProviderClientSecret, Required: false, ConfirmPrompt: false), Logger);
        if (string.IsNullOrEmpty(clientSecret))
        {
            clientSecret = null;
        }

        IdentityProviderDto newIdentityProviderDto;

        if (identityProviderDto is GoogleIdentityProviderDto existingGoogle)
        {
            newIdentityProviderDto = new GoogleIdentityProviderDto
            {
                IsEnabled = isEnabled,
                ClientId = clientId ?? existingGoogle.ClientId,
                ClientSecret = clientSecret,
                Name = name
            };
        }
        else if (identityProviderDto is MicrosoftIdentityProviderDto existingMicrosoft)
        {
            newIdentityProviderDto = new MicrosoftIdentityProviderDto
            {
                IsEnabled = isEnabled,
                ClientId = clientId ?? existingMicrosoft.ClientId,
                ClientSecret = clientSecret,
                Name = name
            };
        }
        else if (identityProviderDto is FacebookIdentityProviderDto existingFacebook)
        {
            newIdentityProviderDto = new FacebookIdentityProviderDto
            {
                IsEnabled = isEnabled,
                ClientId = clientId ?? existingFacebook.ClientId,
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
                ClientSecret = clientSecret,
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
