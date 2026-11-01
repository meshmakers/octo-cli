using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.IdentityProviders;

internal class AddOAuthIdentityProvider : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _allowSelfRegistration;
    private readonly IArgument _clientId;
    private readonly IArgument _clientSecret;
    private readonly IArgument _clientSecretStdin;
    private readonly SecretInput _secretInput;
    private readonly IArgument _defaultGroupRtId;
    private readonly IArgument _enabled;
    private readonly IArgument _name;
    private readonly IArgument _type;

    public AddOAuthIdentityProvider(ILogger<AddOAuthIdentityProvider> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService,
        SecretInput secretInput)
        : base(logger, Constants.IdentityServicesGroup, "AddOAuthIdentityProvider", "Adds a new identity provider.",
            options, identityServicesClient,
            authenticationService)
    {
        _secretInput = secretInput;
        _name = CommandArgumentValue.AddArgument("n", "name", ["Name of identity provider, must be unique"],
            true,
            1);
        _enabled = CommandArgumentValue.AddArgument("e", "enabled",
            ["True if identity provider should be enabled, otherwise false"], true,
            1);
        _clientId = CommandArgumentValue.AddArgument("cid", "clientId",
            ["ServiceClient ID, provided by provider"], true, 1);
        _clientSecret = CommandArgumentValue.AddArgument("cs", "clientSecret",
            [
                "Deprecated: client secret, provided by the provider. A value on the command line ends up in the shell history and the process list.",
                $"Omit it to be prompted (hidden input), or use --client-secret-stdin or the environment variable {Constants.EnvVarIdentityProviderClientSecret}."
            ], false, 1);
        _clientSecretStdin = CommandArgumentValue.AddArgument("css", "client-secret-stdin",
            ["Read the client secret from standard input (one value; a trailing line break is removed)"], false, 0);
        _type = CommandArgumentValue.AddArgument("t", "type",
            ["Type of provider, available are 'google', 'microsoft', 'facebook'"], true, 1);
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
                    new CodeSampleArgument(_name, "Google Login"),
                    new CodeSampleArgument(_type, "google"),
                    new CodeSampleArgument(_clientId, "your-client-id"),
                    new CodeSampleArgument(_clientSecretStdin),
                    new CodeSampleArgument(_enabled, "true"),
                    new CodeSampleArgument(_allowSelfRegistration, "true"),
                    new CodeSampleArgument(_defaultGroupRtId, "<default-group-rtid>"),
                ],
                    description: "Supports Google, Microsoft, Facebook; the client secret is piped in on standard input"),
            ],
            Notes:
            [
                $"The client secret is read from a hidden prompt, from standard input (--client-secret-stdin) or from the environment variable {Constants.EnvVarIdentityProviderClientSecret}, so it does not end up in the shell history or the process list. -cs still works in this release but logs a deprecation warning and will be removed.",
            ]
        );

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_name);
        var type = CommandArgumentValue.GetArgumentScalarValue<IdentityProviderTypesDto>(_type);
        var isEnabled = CommandArgumentValue.GetArgumentScalarValue<bool>(_enabled);
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientId);
        var clientSecret = _secretInput.Resolve(CommandArgumentValue,
            new SecretSpec("client secret", _clientSecret, _clientSecretStdin,
                Constants.EnvVarIdentityProviderClientSecret, Required: true, ConfirmPrompt: true), Logger);

        bool? allowSelfRegistration = CommandArgumentValue.IsArgumentUsed(_allowSelfRegistration)
            ? CommandArgumentValue.GetArgumentScalarValue<bool>(_allowSelfRegistration)
            : null;
        string? defaultGroupRtId = CommandArgumentValue.IsArgumentUsed(_defaultGroupRtId)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_defaultGroupRtId)
            : null;

        Logger.LogInformation("Creating OAuth identity provider \'{Name}\' at \'{ServiceClientServiceUri}\'", name,
            ServiceClient.ServiceUri);

        IdentityProviderDto identityProviderDto;
        if (type == IdentityProviderTypesDto.Google)
        {
            identityProviderDto = new GoogleIdentityProviderDto
            {
                IsEnabled = isEnabled,
                ClientId = clientId,
                ClientSecret = clientSecret,
                Name = name
            };
        }
        else if (type == IdentityProviderTypesDto.Microsoft)
        {
            identityProviderDto = new MicrosoftIdentityProviderDto
            {
                IsEnabled = isEnabled,
                ClientId = clientId,
                ClientSecret = clientSecret,
                Name = name
            };
        }
        else if (type == IdentityProviderTypesDto.Facebook)
        {
            identityProviderDto = new FacebookIdentityProviderDto
            {
                IsEnabled = isEnabled,
                ClientId = clientId,
                ClientSecret = clientSecret,
                Name = name
            };
        }
        else
        {
            Logger.LogError("Unsupported OAuth provider type '{Type}'", type);
            return;
        }

        if (allowSelfRegistration.HasValue)
        {
            identityProviderDto.AllowSelfRegistration = allowSelfRegistration.Value;
        }

        if (defaultGroupRtId != null)
        {
            identityProviderDto.DefaultGroupRtId = defaultGroupRtId;
        }

        await ServiceClient.CreateIdentityProvider(identityProviderDto);

        Logger.LogInformation("Identity provider \'{Name}\' at \'{ServiceClientServiceUri}\' created", name,
            ServiceClient.ServiceUri);
    }
}