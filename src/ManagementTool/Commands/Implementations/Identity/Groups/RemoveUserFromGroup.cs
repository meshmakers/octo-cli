using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Groups;

internal class RemoveUserFromGroup : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _id;
    private readonly IArgument _userId;

    public RemoveUserFromGroup(ILogger<RemoveUserFromGroup> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "RemoveUserFromGroup", "Removes a user from a group.",
            options, identityServicesClient, authenticationService)
    {
        _id = CommandArgumentValue.AddArgument("id", "identifier", ["ID of the group"], true, 1);
        _userId = CommandArgumentValue.AddArgument("uid", "userId", ["ID of the user to remove"], true, 1);
    }

    public override async Task Execute()
    {
        var rtId = CommandArgumentValue.GetArgumentScalarValue<OctoObjectId>(_id);
        var userId = CommandArgumentValue.GetArgumentScalarValue<string>(_userId);

        Logger.LogInformation("Removing user '{UserId}' from group '{RtId}' at '{ServiceClientServiceUri}'",
            userId, rtId, ServiceClient.ServiceUri);

        await ServiceClient.RemoveUserFromGroup(rtId, userId);

        Logger.LogInformation("User '{UserId}' removed from group '{RtId}'", userId, rtId);
    }
}
