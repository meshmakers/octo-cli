using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.IdentityProviders;

internal class AddOctoTenantIdentityProvider : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _allowSelfRegistration;
    private readonly IArgument _defaultGroupRtId;
    private readonly IArgument _enabled;
    private readonly IArgument _name;
    private readonly IArgument _parentTenantId;

    public AddOctoTenantIdentityProvider(ILogger<AddOctoTenantIdentityProvider> logger,
        IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "AddOctoTenantIdentityProvider",
            "Adds a new identity provider for cross-tenant authentication via a parent tenant.", options,
            identityServicesClient, authenticationService)
    {
        _name = CommandArgumentValue.AddArgument("n", "name", ["Name of identity provider, must be unique"],
            true, 1);
        _enabled = CommandArgumentValue.AddArgument("e", "enabled",
            ["True if identity provider should be enabled, otherwise false"], true, 1);
        _parentTenantId = CommandArgumentValue.AddArgument("ptid", "parentTenantId",
            ["ID of the parent tenant to authenticate against"], true, 1);
        _allowSelfRegistration = CommandArgumentValue.AddArgument("asr", "allowSelfRegistration",
            ["Allow self registration (default: true)"], false, 1);
        _defaultGroupRtId = CommandArgumentValue.AddArgument("dgid", "defaultGroupRtId",
            ["Default group RtId for new users"], false, 1);
    }

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_name);

        Logger.LogInformation(
            "Creating OctoTenant identity provider '{Name}' at '{ServiceClientServiceUri}'", name,
            ServiceClient.ServiceUri);

        var identityProviderDto = new OctoTenantIdentityProviderDto
        {
            Name = name,
            IsEnabled = CommandArgumentValue.GetArgumentScalarValue<bool>(_enabled),
            ParentTenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_parentTenantId)
        };

        if (CommandArgumentValue.IsArgumentUsed(_allowSelfRegistration))
        {
            identityProviderDto.AllowSelfRegistration =
                CommandArgumentValue.GetArgumentScalarValue<bool>(_allowSelfRegistration);
        }

        if (CommandArgumentValue.IsArgumentUsed(_defaultGroupRtId))
        {
            identityProviderDto.DefaultGroupRtId =
                CommandArgumentValue.GetArgumentScalarValue<string>(_defaultGroupRtId);
        }

        await ServiceClient.CreateIdentityProvider(identityProviderDto);

        Logger.LogInformation("Identity provider '{Name}' at '{ServiceClientServiceUri}' created", name,
            ServiceClient.ServiceUri);
    }
}
