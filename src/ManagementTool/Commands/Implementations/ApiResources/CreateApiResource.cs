using System;
using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Common.Shared.DataTransferObjects;
using Meshmakers.Octo.Frontend.Client.System;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.ApiScopes;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.ApiResources;

internal class CreateApiResource : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _nameArg;
    private readonly IArgument _displayNameArg;
    private readonly IArgument _descriptionArg;
    private readonly IArgument _scopesArg;
    
    public CreateApiResource(ILogger<CreateApiResource> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, "CreateApiResource", "Adds a new api resource.", options,
            identityServicesClient, authenticationService)
    {
        _nameArg = CommandArgumentValue.AddArgument("n", "name", new[] { "Name of resource, must be unique" },
            true,
            1);
        _displayNameArg =
            CommandArgumentValue.AddArgument("dn", "displayName", new[] { "Display name of resource" }, false, 1);
        _descriptionArg =
            CommandArgumentValue.AddArgument("d", "description", new[] { "Description of scope resource" }, false, 1);
        
        _scopesArg = 
            CommandArgumentValue.AddArgument("s", "scopes", new[] { "Scopes to add to resource. Split them with ," }, false, 1);
    }

    public override async Task Execute()
    {
        var resourceName = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg);

        Logger.LogInformation("Creating API resource \'{ApiResource}\' at \'{ServiceClientServiceUri}\'", resourceName,
            ServiceClient.ServiceUri);

        var scopes = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_scopesArg)?.Split(",", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        
        var apiScopeDto = new ApiResourceDto()
        {
            Name = resourceName,
            DisplayName = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_displayNameArg),
            Description = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_descriptionArg),
            Scopes = scopes ?? Array.Empty<string>(),
            IsEnabled = true,
            ShowInDiscoveryDocument = true
        };

        await ServiceClient.CreateApiResource(apiScopeDto);

        Logger.LogInformation("API resource \'{ResourceName}\' at \'{ServiceClientServiceUri}\' created", resourceName,
            ServiceClient.ServiceUri);
    }
}