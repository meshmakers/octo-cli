using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Roles;

internal class UpdateRole : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IdentityMethodCall _methods;
    private readonly IArgument _nameArg;
    private readonly IArgument _newNameArg;

    public UpdateRole(ILogger<UpdateRole> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "UpdateRole", "Updates a role", options, identityServicesClient,
            authenticationService)
    {
        _methods = new IdentityMethodCall(tenantClient, authenticationService, logger);
        _nameArg = CommandArgumentValue.AddArgument("n", "name", ["Name of role"], true,
            1);
        _newNameArg = CommandArgumentValue.AddArgument("nn", "newRoleName",
            ["New name of role"], false,
            1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_nameArg, "DataAnalyst"),
                    new CodeSampleArgument(_newNameArg, "SeniorDataAnalyst"),
                ],
                    description: "Basic usage"),
            ]
        );

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg).ToLower();

        string? newRoleName = null;
        if (CommandArgumentValue.IsArgumentUsed(_newNameArg))
        {
            newRoleName = CommandArgumentValue.GetArgumentScalarValue<string>(_newNameArg).ToLower();
        }

        Logger.LogInformation("Updating role \'{Name}\' at \'{ServiceClientServiceUri}\'", name,
            ServiceClient.ServiceUri);

        var roleDto = new RoleDto
        {
            Name = newRoleName
        };

        await _methods.RunAsync(RtRoleMethodClient.UpdateRoleMethodId,
            async () => await new RtRoleMethodClient(_methods.TenantClient).UpdateRoleAsync(
                await IdentityMethodCall.ResolveRoleRtIdAsync(ServiceClient, name), newRoleName ?? string.Empty));

        Logger.LogInformation("Role \'{Name}\' updated", name);
    }
}