using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.IdentityProviders;

internal class UpdateIdentityProvider : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _alias;
    private readonly IArgument _clientId;
    private readonly IArgument _clientSecret;
    private readonly IArgument _enabled;
    private readonly IArgument _id;

    public UpdateIdentityProvider(ILogger<UpdateIdentityProvider> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "UpdateIdentityProvider", "Updates an identity provider.", options, identityServicesClient,
            authenticationService)
    {
        _id = CommandArgumentValue.AddArgument("id", "identifier",
            new[] { "ID of identity provider, must be unique" }, true,
            1);
        _alias = CommandArgumentValue.AddArgument("a", "alias",
            new[] { "Alias of identity provider, must be unique" }, true,
            1);
        _enabled = CommandArgumentValue.AddArgument("e", "enabled",
            new[] { "True if identity provider should be enabled, otherwise false" }, true,
            1);
        _clientId = CommandArgumentValue.AddArgument("cid", "clientId",
            new[] { "ServiceClient ID, provided by provider" }, true, 1);
        _clientSecret = CommandArgumentValue.AddArgument("cs", "clientSecret",
            new[] { "ServiceClient secret, provided by provider" }, true, 1);
    }

    public override async Task Execute()
    {
        var id = CommandArgumentValue.GetArgumentScalarValue<string>(_id);

        Logger.LogInformation("Updating identity provider \'{Id}\' at \'{ServiceClientServiceUri}\'", id,
            ServiceClient.ServiceUri);

        var identityProviderDto = await ServiceClient.GetIdentityProvider(id);
        if (identityProviderDto == null)
        {
            Logger.LogError("Identity provider \'{Id}\' at \'{ServiceClientServiceUri}\' not found", id,
                ServiceClient.ServiceUri);
            return;
        }

        if (identityProviderDto is GoogleIdentityProviderDto)
        {
            var newIdentityProviderDto = new GoogleIdentityProviderDto
            {
                Id = identityProviderDto.Id,
                IsEnabled = CommandArgumentValue.GetArgumentScalarValue<bool>(_enabled),
                ClientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientId),
                ClientSecret = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_clientSecret),
                Alias = CommandArgumentValue.GetArgumentScalarValue<string>(_alias)
            };
            await ServiceClient.UpdateIdentityProvider(id, newIdentityProviderDto);
        }
        else if (identityProviderDto is MicrosoftIdentityProviderDto)
        {
            var newIdentityProviderDto = new MicrosoftIdentityProviderDto
            {
                IsEnabled = CommandArgumentValue.GetArgumentScalarValue<bool>(_enabled),
                ClientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientId),
                ClientSecret = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_clientSecret),
                Alias = CommandArgumentValue.GetArgumentScalarValue<string>(_alias)
            };
            await ServiceClient.UpdateIdentityProvider(id, newIdentityProviderDto);
        }

        Logger.LogInformation("Identity provider \'{Id}\' at \'{ServiceClientServiceUri}\' updated", id,
            ServiceClient.ServiceUri);
    }
}
