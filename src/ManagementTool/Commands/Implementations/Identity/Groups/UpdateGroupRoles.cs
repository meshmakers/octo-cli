using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Groups;

internal class UpdateGroupRoles : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _id;
    private readonly IArgument _roleIds;

    public UpdateGroupRoles(ILogger<UpdateGroupRoles> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "UpdateGroupRoles",
            "Updates the roles assigned to a group.", options, identityServicesClient, authenticationService)
    {
        _id = CommandArgumentValue.AddArgument("id", "identifier", ["ID of the group"], true, 1);
        _roleIds = CommandArgumentValue.AddArgument("rids", "roleIds",
            ["Comma-separated list of role IDs to assign"], true, 1);
    }

    public override async Task Execute()
    {
        var rtId = CommandArgumentValue.GetArgumentScalarValue<OctoObjectId>(_id);
        var roleIdsValue = CommandArgumentValue.GetArgumentScalarValue<string>(_roleIds);
        var roleIds = roleIdsValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        Logger.LogInformation("Updating roles for group '{RtId}' at '{ServiceClientServiceUri}'",
            rtId, ServiceClient.ServiceUri);

        await ServiceClient.UpdateGroupRoles(rtId, roleIds);

        Logger.LogInformation("Group '{RtId}' roles updated", rtId);
    }
}
