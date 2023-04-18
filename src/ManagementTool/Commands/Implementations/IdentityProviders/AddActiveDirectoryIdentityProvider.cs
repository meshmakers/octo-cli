using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Common.Shared.DataTransferObjects;
using Meshmakers.Octo.Frontend.Client.System;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.IdentityProviders;

internal class AddActiveDirectoryIdentityProvider : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _alias;
    private readonly IArgument _enabled;
    private readonly IArgument _host;
    private readonly IArgument _port;
    private readonly IArgument _accountName;
    private readonly IArgument _accountPassword;

    public AddActiveDirectoryIdentityProvider(ILogger<AddActiveDirectoryIdentityProvider> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "AddAdIdentityProvider", "Adds a new identity provider for active directory.", options, identityServicesClient,
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
        _accountName = CommandArgumentValue.AddArgument("u", "userPrincipalName",
            new[] { "Name for machine account for authentication" }, true, 1);
        _accountPassword = CommandArgumentValue.AddArgument("psw", "password",
            new[] { "Password for machine account for authentication" }, true, 1);
    }

    public override async Task Execute()
    {
        var alias = CommandArgumentValue.GetArgumentScalarValue<string>(_alias);

        Logger.LogInformation("Creating Active Directory identity provider \'{Alias}\' at \'{ServiceClientServiceUri}\'", alias,
            ServiceClient.ServiceUri);

            var identityProviderDto = new MicrosoftAdProviderDto
            {
                IsEnabled = CommandArgumentValue.GetArgumentScalarValue<bool>(_enabled),
                Host = CommandArgumentValue.GetArgumentScalarValue<string>(_host),
                Port = CommandArgumentValue.GetArgumentScalarValue<ushort>(_port),
                UserPrincipalName = CommandArgumentValue.GetArgumentScalarValue<string>(_accountName),
                Password = CommandArgumentValue.GetArgumentScalarValue<string>(_accountPassword),
                Alias = alias
            };
            await ServiceClient.CreateIdentityProvider(identityProviderDto);
        
        Logger.LogInformation("ServiceClient \'{Alias}\' at \'{ServiceClientServiceUri}\' created", alias,
            ServiceClient.ServiceUri);
    }
}
