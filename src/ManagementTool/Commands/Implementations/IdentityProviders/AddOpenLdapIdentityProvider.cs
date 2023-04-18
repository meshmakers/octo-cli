using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Common.Shared.DataTransferObjects;
using Meshmakers.Octo.Frontend.Client.System;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.IdentityProviders;

internal class AddOpenLdapIdentityProvider : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _alias;
    private readonly IArgument _enabled;
    private readonly IArgument _host;
    private readonly IArgument _port;
    private readonly IArgument _accountName;
    private readonly IArgument _accountPassword;
    private readonly IArgument _userBaseDn;
    private readonly IArgument _userNameAttribute;

    public AddOpenLdapIdentityProvider(ILogger<AddOpenLdapIdentityProvider> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "AddOpenLdapIdentityProvider", "Adds a new identity provider for Open LDAP.", options, identityServicesClient,
            authenticationService)
    {
        _alias = CommandArgumentValue.AddArgument("a", "alias", new[] { "Alias of identity provider, must be unique" },
            true,
            1);
        _enabled = CommandArgumentValue.AddArgument("e", "enabled",
            new[] { "True if identity provider should be enabled, otherwise false" }, true,
            1);
        _host = CommandArgumentValue.AddArgument("h", "host",
            new[] { "Host" }, true, 1);
        _port = CommandArgumentValue.AddArgument("p", "port",
            new[] { "Host" }, true, 1);
        _accountName = CommandArgumentValue.AddArgument("u", "userDistinguishedName",
            new[] { "DN for machine account for authentication" }, true, 1);
        _accountPassword = CommandArgumentValue.AddArgument("psw", "password",
            new[] { "Password for machine account for authentication" }, true, 1);
        _userBaseDn = CommandArgumentValue.AddArgument("ubdn", "userBaseDn",
            new[] { "User base DN, e. g. cn=users,dc=meshmakers,dc=cloud" }, true, 1);
        _userNameAttribute = CommandArgumentValue.AddArgument("uan", "userAttributeName",
            new[] { "User name attribute name, e. g. uid" }, true, 1);
    }

    public override async Task Execute()
    {
        var alias = CommandArgumentValue.GetArgumentScalarValue<string>(_alias);

        Logger.LogInformation("Creating OpenLDAP identity provider \'{Alias}\' at \'{ServiceClientServiceUri}\'", alias,
            ServiceClient.ServiceUri);

            var identityProviderDto = new OpenLdapProviderDto
            {
                IsEnabled = CommandArgumentValue.GetArgumentScalarValue<bool>(_enabled),
                Host = CommandArgumentValue.GetArgumentScalarValue<string>(_host),
                Port = CommandArgumentValue.GetArgumentScalarValue<ushort>(_port),
                UserDistinguishedName = CommandArgumentValue.GetArgumentScalarValue<string>(_accountName),
                Password = CommandArgumentValue.GetArgumentScalarValue<string>(_accountPassword),
                UserBaseDn = CommandArgumentValue.GetArgumentScalarValue<string>(_userBaseDn),
                UserNameAttribute = CommandArgumentValue.GetArgumentScalarValue<string>(_userNameAttribute),
                Alias = alias
            };
            await ServiceClient.CreateIdentityProvider(identityProviderDto);
        
        Logger.LogInformation("ServiceClient \'{Alias}\' at \'{ServiceClientServiceUri}\' created", alias,
            ServiceClient.ServiceUri);
    }
}
