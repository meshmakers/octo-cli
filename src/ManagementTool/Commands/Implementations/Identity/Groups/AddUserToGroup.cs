using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Groups;

internal class AddUserToGroup : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _id;
    private readonly IArgument _userId;

    public AddUserToGroup(ILogger<AddUserToGroup> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "AddUserToGroup", "Adds a user to a group.", options,
            identityServicesClient, authenticationService)
    {
        _id = CommandArgumentValue.AddArgument("id", "identifier", ["ID of the group"], true, 1);
        _userId = CommandArgumentValue.AddArgument("uid", "userId", ["ID of the user to add"], true, 1);
    }

    public override async Task Execute()
    {
        var rtId = CommandArgumentValue.GetArgumentScalarValue<OctoObjectId>(_id);
        var userId = CommandArgumentValue.GetArgumentScalarValue<string>(_userId);

        Logger.LogInformation("Adding user '{UserId}' to group '{RtId}' at '{ServiceClientServiceUri}'",
            userId, rtId, ServiceClient.ServiceUri);

        await ServiceClient.AddUserToGroup(rtId, userId);

        Logger.LogInformation("User '{UserId}' added to group '{RtId}'", userId, rtId);
    }
}
