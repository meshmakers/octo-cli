using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.IdentityProviders;

internal class AddAzureEntryIdIdentityProvider : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _name;
    private readonly IArgument _tenantId;
    private readonly IArgument _clientId;
    private readonly IArgument _clientSecret;
    private readonly IArgument _enabled;

    public AddAzureEntryIdIdentityProvider(ILogger<AddAzureEntryIdIdentityProvider> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "AddAzureEntryIdIdentityProvider",
            "Adds a new identity provider for Microsoft Azure Entra ID.", options, identityServicesClient,
            authenticationService)
    {
        _name = CommandArgumentValue.AddArgument("n", "name", ["Name of identity provider, must be unique"],
            true,
            1);
        _tenantId = CommandArgumentValue.AddArgument("t", "tenantId", ["Tenant ID of Azure Entry ID"],
            true,
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
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_name);

        Logger.LogInformation("Creating Microsoft Azure Entry ID identity provider \'{Name}\' at \'{ServiceClientServiceUri}\'", name,
            ServiceClient.ServiceUri);

        var identityProviderDto = new AzureEntraIdProviderDto
        {
            Name = name,
            IsEnabled = CommandArgumentValue.GetArgumentScalarValue<bool>(_enabled),
            TenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantId),
            ClientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientId),
            ClientSecret = CommandArgumentValue.GetArgumentScalarValue<string>(_clientSecret)
        };
        await ServiceClient.CreateIdentityProvider(identityProviderDto);

        Logger.LogInformation("Identity provider \'{Name}\' at \'{ServiceClientServiceUri}\' created", name,
            ServiceClient.ServiceUri);
    }
}