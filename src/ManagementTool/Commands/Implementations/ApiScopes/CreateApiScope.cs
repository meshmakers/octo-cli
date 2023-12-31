using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.ApiScopes;

internal class CreateApiScope : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _descriptionArg;
    private readonly IArgument _displayNameArg;
    private readonly IArgument _isEnabledArg;
    private readonly IArgument _nameArg;

    public CreateApiScope(ILogger<CreateApiScope> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "CreateApiScope", "Adds a new client using grant type 'ClientCredentials'.", options,
            identityServicesClient, authenticationService)
    {
        _nameArg = CommandArgumentValue.AddArgument("n", "name", new[] { "Name of scope, must be unique" },
            true,
            1);
        _isEnabledArg = CommandArgumentValue.AddArgument("e", "enabled",
            new[] { "false for disabled, true for enabled." }, false, 1);
        _displayNameArg =
            CommandArgumentValue.AddArgument("dn", "displayName", new[] { "Display name of scope" }, false, 1);
        _descriptionArg =
            CommandArgumentValue.AddArgument("d", "description", new[] { "Description of scope scope" }, false, 1);
    }

    public override async Task Execute()
    {
        var scopeName = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg);

        Logger.LogInformation("Creating API scope \'{ApiScope}\' at \'{ServiceClientServiceUri}\'", scopeName,
            ServiceClient.ServiceUri);

        var apiScopeDto = new ApiScopeDto
        {
            IsEnabled = !CommandArgumentValue.IsArgumentUsed(_isEnabledArg) ||
                        CommandArgumentValue.GetArgumentScalarValueOrDefault<bool>(_isEnabledArg),
            Name = scopeName,
            DisplayName = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_displayNameArg),
            Description = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_descriptionArg),
            ShowInDiscoveryDocument = true
        };

        await ServiceClient.CreateApiScope(apiScopeDto);

        Logger.LogInformation("API scope \'{ScopeName}\' at \'{ServiceClientServiceUri}\' created", scopeName,
            ServiceClient.ServiceUri);
    }
}
