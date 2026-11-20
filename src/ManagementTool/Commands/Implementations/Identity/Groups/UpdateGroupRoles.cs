using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Groups;

internal class UpdateGroupRoles : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IdentityMethodCall _methods;
    private readonly IArgument _id;
    private readonly IArgument _roleIds;

    public UpdateGroupRoles(ILogger<UpdateGroupRoles> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "UpdateGroupRoles",
            "Updates the roles assigned to a group.", options, identityServicesClient, authenticationService)
    {
        _methods = new IdentityMethodCall(tenantClient, authenticationService, logger);
        _id = CommandArgumentValue.AddArgument("id", "identifier", ["ID of the group"], true, 1);
        _roleIds = CommandArgumentValue.AddArgument("rids", "roleIds",
            ["Comma-separated list of role IDs to assign"], true, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_id, "<group-rtid>"),
                    new CodeSampleArgument(_roleIds, "<role-rtid>,<role-rtid>,<role-rtid>"),
                ],
                    description: "Replace all roles assigned to a group"),
            ]
        );

    public override async Task Execute()
    {
        var rtId = OctoObjectId.Parse(CommandArgumentValue.GetArgumentScalarValue<string>(_id));
        var roleIdsValue = CommandArgumentValue.GetArgumentScalarValue<string>(_roleIds);
        var roleIds = roleIdsValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        Logger.LogInformation("Updating roles for group '{RtId}' at '{ServiceClientServiceUri}'",
            rtId, ServiceClient.ServiceUri);

        var roleNames = await IdentityMethodCall.ResolveRoleNamesAsync(ServiceClient, roleIds);
        await _methods.RunAsync(RtGroupMethodClient.SetRolesMethodId,
            () => new RtGroupMethodClient(_methods.TenantClient).SetRolesAsync(rtId.ToString(), roleNames));

        Logger.LogInformation("Group '{RtId}' roles updated", rtId);
    }
}
