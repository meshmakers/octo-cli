using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.ApiResources;

internal class UpdateApiResource : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _descriptionArg;
    private readonly IArgument _displayNameArg;
    private readonly IArgument _nameArg;
    private readonly IArgument _scopesArg;

    public UpdateApiResource(ILogger<UpdateApiResource> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "UpdateApiResource", "Updates an api resource.", options,
            identityServicesClient, authenticationService)
    {
        _nameArg = CommandArgumentValue.AddArgument("n", "name", ["Name of resource, must be unique"],
            true,
            1);

        _displayNameArg =
            CommandArgumentValue.AddArgument("dn", "displayName", ["Display name of resource"], false, 1);

        _descriptionArg =
            CommandArgumentValue.AddArgument("d", "description", ["Description of scope resource"], false, 1);

        _scopesArg =
            CommandArgumentValue.AddArgument("s", "scopes", ["Scopes to add to resource. Split them with ,"], false, 1);
    }

    public override async Task Execute()
    {
        var resourceName = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg);

        Logger.LogInformation("Update API resource \'{ApiResource}\' at \'{ServiceClientServiceUri}\'", resourceName,
            ServiceClient.ServiceUri);

        var scopes = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_scopesArg)?
            .Split(",", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var apiScopeDto = new ApiResourceDto
        {
            Name = resourceName,
            DisplayName = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_displayNameArg),
            Description = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_descriptionArg),
            Scopes = scopes ?? Array.Empty<string>(),
            ShowInDiscoveryDocument = true
        };

        await ServiceClient.UpdateApiResource(resourceName, apiScopeDto);

        Logger.LogInformation("API resource \'{ResourceName}\' at \'{ServiceClientServiceUri}\' updated", resourceName,
            ServiceClient.ServiceUri);
    }
}