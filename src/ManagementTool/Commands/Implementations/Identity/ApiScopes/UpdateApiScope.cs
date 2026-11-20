using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.ApiScopes;

internal class UpdateApiScope : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IdentityMethodCall _methods;
    private readonly IArgument _descriptionArg;
    private readonly IArgument _displayNameArg;
    private readonly IArgument _isEnabledArg;
    private readonly IArgument _nameArg;
    private readonly IArgument _newNameArg;

    public UpdateApiScope(ILogger<UpdateApiScope> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "UpdateApiScope", "Updates an API scope.", options,
            identityServicesClient,
            authenticationService)
    {
        _methods = new IdentityMethodCall(tenantClient, authenticationService, logger);
        _nameArg = CommandArgumentValue.AddArgument("n", "name", ["Name of scope, must be unique"],
            true,
            1);
        _isEnabledArg = CommandArgumentValue.AddArgument("e", "enabled",
            ["false for disabled, true for enabled."], false, 1);
        _newNameArg = CommandArgumentValue.AddArgument("nn", "newName", ["New name of scope, must be unique"],
            false, 1);
        _displayNameArg =
            CommandArgumentValue.AddArgument("dn", "displayName", ["Display name of scope"], false, 1);
        _descriptionArg =
            CommandArgumentValue.AddArgument("d", "description", ["Description of API scope"], false, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_nameArg, "myAPI.admin"),
                    new CodeSampleArgument(_newNameArg, "myAPI.superadmin"),
                ],
                    description: "Basic usage"),
            ]
        );

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

        await _methods.RunAsync(RtApiScopeMethodClient.UpdateApiScopeMethodId,
            async () => await new RtApiScopeMethodClient(_methods.TenantClient).UpdateApiScopeAsync(
                await _methods.ResolveApiScopeRtIdAsync(scopeName), scopeDto.Name ?? string.Empty, scopeDto.DisplayName,
                scopeDto.Description, scopeDto.IsEnabled, scopeDto.ShowInDiscoveryDocument, scopeDto.IsRequired,
                scopeDto.IsEmphasize, scopeDto.UserClaims?.ToList()));

        Logger.LogInformation("API scope \'{ScopeName}\' at \'{ServiceClientServiceUri}\' updated", scopeName,
            ServiceClient.ServiceUri);
    }
}