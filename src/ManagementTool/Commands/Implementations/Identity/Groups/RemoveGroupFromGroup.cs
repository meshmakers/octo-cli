using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Groups;

internal class RemoveGroupFromGroup : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _childGroupId;
    private readonly IArgument _id;

    public RemoveGroupFromGroup(ILogger<RemoveGroupFromGroup> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "RemoveGroupFromGroup",
            "Removes a child group from a parent group.", options, identityServicesClient, authenticationService)
    {
        _id = CommandArgumentValue.AddArgument("id", "identifier", ["ID of the parent group"], true, 1);
        _childGroupId = CommandArgumentValue.AddArgument("cgid", "childGroupId",
            ["ID of the child group to remove"], true, 1);
    }

    public override async Task Execute()
    {
        var rtId = CommandArgumentValue.GetArgumentScalarValue<OctoObjectId>(_id);
        var childGroupId = CommandArgumentValue.GetArgumentScalarValue<string>(_childGroupId);

        Logger.LogInformation(
            "Removing child group '{ChildGroupId}' from group '{RtId}' at '{ServiceClientServiceUri}'",
            childGroupId, rtId, ServiceClient.ServiceUri);

        await ServiceClient.RemoveGroupFromGroup(rtId, childGroupId);

        Logger.LogInformation("Child group '{ChildGroupId}' removed from group '{RtId}'", childGroupId, rtId);
    }
}
