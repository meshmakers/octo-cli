using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.IdentityProviders;

internal class AddOpenLdapIdentityProvider : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _enabled;
    private readonly IArgument _host;
    private readonly IArgument _name;
    private readonly IArgument _port;
    private readonly IArgument _userBaseDn;
    private readonly IArgument _userNameAttribute;

    public AddOpenLdapIdentityProvider(ILogger<AddOpenLdapIdentityProvider> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "AddOpenLdapIdentityProvider",
            "Adds a new identity provider for Open LDAP.", options, identityServicesClient,
            authenticationService)
    {
        _name = CommandArgumentValue.AddArgument("n", "name", ["Name of identity provider, must be unique"],
            true,
            1);
        _enabled = CommandArgumentValue.AddArgument("e", "enabled",
            ["True if identity provider should be enabled, otherwise false"], true,
            1);
        _host = CommandArgumentValue.AddArgument("h", "host",
            ["Host"], true, 1);
        _port = CommandArgumentValue.AddArgument("p", "port",
            ["Port of host"], true, 1);
        _userBaseDn = CommandArgumentValue.AddArgument("ubdn", "userBaseDn",
            ["User base DN, e. g. cn=users,dc=meshmakers,dc=cloud"], true, 1);
        _userNameAttribute = CommandArgumentValue.AddArgument("uan", "userAttributeName",
            ["User name attribute name, e. g. uid"], true, 1);
    }

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_name);

        Logger.LogInformation("Creating OpenLDAP identity provider \'{Name}\' at \'{ServiceClientServiceUri}\'", name,
            ServiceClient.ServiceUri);

        var identityProviderDto = new OpenLdapProviderDto
        {
            IsEnabled = CommandArgumentValue.GetArgumentScalarValue<bool>(_enabled),
            Host = CommandArgumentValue.GetArgumentScalarValue<string>(_host),
            Port = CommandArgumentValue.GetArgumentScalarValue<ushort>(_port),
            UserBaseDn = CommandArgumentValue.GetArgumentScalarValue<string>(_userBaseDn),
            UserNameAttribute = CommandArgumentValue.GetArgumentScalarValue<string>(_userNameAttribute),
            Name = name
        };
        await ServiceClient.CreateIdentityProvider(identityProviderDto);

        Logger.LogInformation("Identity provider \'{Name}\' at \'{ServiceClientServiceUri}\' created", name,
            ServiceClient.ServiceUri);
    }
}