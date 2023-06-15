using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.ApiScopes;

internal class UpdateApiScope : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _descriptionArg;
    private readonly IArgument _displayNameArg;
    private readonly IArgument _isEnabledArg;
    private readonly IArgument _nameArg;
    private readonly IArgument _newNameArg;

    public UpdateApiScope(ILogger<UpdateApiScope> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "UpdateApiScope", "Updates an API scope.", options, identityServicesClient, authenticationService)
    {
        _nameArg = CommandArgumentValue.AddArgument("n", "name", new[] { "Name of scope, must be unique" },
            true,
            1);
        _isEnabledArg = CommandArgumentValue.AddArgument("e", "enabled",
            new[] { "false for disabled, true for enabled." }, false, 1);
        _newNameArg = CommandArgumentValue.AddArgument("nn", "newName", new[] { "New name of scope, must be unique" },
            false, 1);
        _displayNameArg =
            CommandArgumentValue.AddArgument("dn", "displayName", new[] { "Display name of scope" }, false, 1);
        _descriptionArg =
            CommandArgumentValue.AddArgument("d", "description", new[] { "Description of scope scope" }, false, 1);
    }

    public override async Task Execute()
    {
        var scopeName = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg);

        Logger.LogInformation("Updating API scope \'{ScopeName}\' at \'{ServiceClientServiceUri}\'", scopeName,
            ServiceClient.ServiceUri);

        var scopeDto = await ServiceClient.GetApiScope(scopeName);

        if (CommandArgumentValue.IsArgumentUsed(_newNameArg))
        {
            scopeDto.Name = CommandArgumentValue.GetArgumentScalarValue<string>(_newNameArg);
        }

        if (CommandArgumentValue.IsArgumentUsed(_displayNameArg))
        {
            scopeDto.DisplayName = CommandArgumentValue.GetArgumentScalarValue<string>(_displayNameArg);
        }

        if (CommandArgumentValue.IsArgumentUsed(_descriptionArg))
        {
            scopeDto.Description = CommandArgumentValue.GetArgumentScalarValue<string>(_descriptionArg);
        }

        if (CommandArgumentValue.IsArgumentUsed(_isEnabledArg))
        {
            scopeDto.IsEnabled = CommandArgumentValue.GetArgumentScalarValue<bool>(_isEnabledArg);
        }

        await ServiceClient.UpdateApiScope(scopeName, scopeDto);

        Logger.LogInformation("API scope \'{ScopeName}\' at \'{ServiceClientServiceUri}\' updated", scopeName,
            ServiceClient.ServiceUri);
    }
}
