using System.Text.Json;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.IdentityProviders;

internal class AddActiveDirectoryIdentityProvider : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _accountName;
    private readonly IArgument _accountPassword;
    private readonly IArgument _name;
    private readonly IArgument _enabled;
    private readonly IArgument _host;
    private readonly IArgument _port;

    public AddActiveDirectoryIdentityProvider(ILogger<AddActiveDirectoryIdentityProvider> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "AddAdIdentityProvider", "Adds a new identity provider for active directory.", options, identityServicesClient,
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
            ["Host"], true, 1);
        _accountName = CommandArgumentValue.AddArgument("u", "userPrincipalName",
            ["Name for machine account for authentication"], true, 1);
        _accountPassword = CommandArgumentValue.AddArgument("psw", "password",
            ["Password for machine account for authentication"], true, 1);
    }

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_name);

        Logger.LogInformation("Creating Active Directory identity provider \'{Name}\' at \'{ServiceClientServiceUri}\'", name,
            ServiceClient.ServiceUri);

        var identityProviderDto = new MicrosoftAdProviderDto
        {
            IsEnabled = CommandArgumentValue.GetArgumentScalarValue<bool>(_enabled),
            Host = CommandArgumentValue.GetArgumentScalarValue<string>(_host),
            Port = CommandArgumentValue.GetArgumentScalarValue<ushort>(_port),
            UserPrincipalName = CommandArgumentValue.GetArgumentScalarValue<string>(_accountName),
            Password = CommandArgumentValue.GetArgumentScalarValue<string>(_accountPassword),
            Name = name
        };
        var json = JsonSerializer.Serialize<IdentityProviderDto>(identityProviderDto);
        await ServiceClient.CreateIdentityProvider(identityProviderDto);

        Logger.LogInformation("ServiceClient \'{Name}\' at \'{ServiceClientServiceUri}\' created", name,
            ServiceClient.ServiceUri);
    }
}
