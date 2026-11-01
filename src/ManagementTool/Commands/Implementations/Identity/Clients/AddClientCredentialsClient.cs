using Duende.IdentityModel;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;

internal class AddClientCredentialsClient : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _clientId;
    private readonly IArgument _clientName;
    private readonly IArgument _clientSecret;
    private readonly IArgument _clientSecretStdin;
    private readonly SecretInput _secretInput;
    private readonly IArgument _autoProvision;

    public AddClientCredentialsClient(ILogger<AddClientCredentialsClient> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService,
        SecretInput secretInput)
        : base(logger, Constants.IdentityServicesGroup, "AddClientCredentialsClient",
            "Adds a new client using grant type 'ClientCredentials'.", options,
            identityServicesClient, authenticationService)
    {
        _secretInput = secretInput;
        _clientId = CommandArgumentValue.AddArgument("id", "clientId", ["ServiceClient ID, must be unique"],
            true,
            1);
        _clientName = CommandArgumentValue.AddArgument("n", "name", ["Display name of client used for grants"],
            true,
            1);
        _clientSecret = CommandArgumentValue.AddArgument("s", "secret",
            [
                "Deprecated: client secret used for client credential authentication. A value on the command line ends up in the shell history and the process list.",
                $"Omit it to be prompted (hidden input), or use --secret-stdin or the environment variable {Constants.EnvVarNewClientSecret}."
            ], false, 1);
        _clientSecretStdin = CommandArgumentValue.AddArgument("ss", "secret-stdin",
            ["Read the client secret from standard input (one value; a trailing line break is removed)"], false, 0);
        _autoProvision = CommandArgumentValue.AddArgument("apic", "autoProvision",
            [
                "If set, this client is auto-provisioned (mirrored) into every new sub-tenant of the calling tenant.",
                "Use for service-to-service / CI-CD identities that must reach many tenants with a single ClientId/secret pair."
            ], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_clientId, "my-service"),
                    new CodeSampleArgument(_clientName, "My Background Service"),
                ],
                    description: "Interactive: asks for the client secret twice, without echoing it"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_clientId, "my-service"),
                    new CodeSampleArgument(_clientName, "My Background Service"),
                    new CodeSampleArgument(_clientSecretStdin),
                ],
                    description: "Automation: pipe the secret in (e.g. from a secret store)"),
            ],
            Notes:
            [
                $"The client secret is read from a hidden prompt, from standard input (--secret-stdin) or from the environment variable {Constants.EnvVarNewClientSecret}, so it does not end up in the shell history or the process list. -s still works in this release but logs a deprecation warning and will be removed. {Constants.EnvVarClientSecret} is not read here: it is the secret octo-cli itself logs in with.",
            ]
        );

    public override async Task Execute()
    {
        var clientSecret = _secretInput.Resolve(CommandArgumentValue,
            new SecretSpec("client secret", _clientSecret, _clientSecretStdin, Constants.EnvVarNewClientSecret,
                Required: true, ConfirmPrompt: true), Logger)!;
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientId);
        var clientName = CommandArgumentValue.GetArgumentScalarValue<string>(_clientName);
        var autoProvision = CommandArgumentValue.IsArgumentUsed(_autoProvision);

        Logger.LogInformation("Creating client \'{ClientId}\' at \'{ServiceClientServiceUri}\'", clientId,
            ServiceClient.ServiceUri);

        var clientDto = new ClientDto
        {
            IsEnabled = true,
            ClientId = clientId,
            ClientName = clientName,
            ClientSecret = clientSecret,
            RequireClientSecret = true,
            AllowedGrantTypes = [OidcConstants.GrantTypes.ClientCredentials],
            AllowedScopes = [CommonConstants.OctoApiFullAccess],
            IsOfflineAccessEnabled = true,
            AutoProvisionInChildTenants = autoProvision ? true : null
        };

        await ServiceClient.CreateClient(clientDto);

        Logger.LogInformation("ServiceClient \'{ClientId}\' at \'{ServiceClientServiceUri}\' created{AutoNote}",
            clientId, ServiceClient.ServiceUri,
            autoProvision ? " (AutoProvisionInChildTenants enabled)" : string.Empty);
    }
}