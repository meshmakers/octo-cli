using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.IdentityProviders;

internal class AddActiveDirectoryIdentityProvider : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _enabled;
    private readonly IArgument _host;
    private readonly IArgument _name;
    private readonly IArgument _port;

    public AddActiveDirectoryIdentityProvider(ILogger<AddActiveDirectoryIdentityProvider> logger,
        IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "AddAdIdentityProvider",
            "Adds a new identity provider for active directory.", options, identityServicesClient,
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
    }

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_name);

        Logger.LogInformation("Creating Active Directory identity provider \'{Name}\' at \'{ServiceClientServiceUri}\'",
            name,
            ServiceClient.ServiceUri);

        var identityProviderDto = new MicrosoftAdProviderDto
        {
            IsEnabled = CommandArgumentValue.GetArgumentScalarValue<bool>(_enabled),
            Host = CommandArgumentValue.GetArgumentScalarValue<string>(_host),
            Port = CommandArgumentValue.GetArgumentScalarValue<ushort>(_port),
            Name = name
        };
        await ServiceClient.CreateIdentityProvider(identityProviderDto);

        Logger.LogInformation("Identity provider \'{Name}\' at \'{ServiceClientServiceUri}\' created", name,
            ServiceClient.ServiceUri);
    }
}