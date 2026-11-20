using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Groups;

internal class RemoveUserFromGroup : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IdentityMethodCall _methods;
    private readonly IArgument _id;
    private readonly IArgument _userId;

    public RemoveUserFromGroup(ILogger<RemoveUserFromGroup> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "RemoveUserFromGroup", "Removes a user from a group.",
            options, identityServicesClient, authenticationService)
    {
        _methods = new IdentityMethodCall(tenantClient, authenticationService, logger);
        _id = CommandArgumentValue.AddArgument("id", "identifier", ["ID of the group"], true, 1);
        _userId = CommandArgumentValue.AddArgument("uid", "userId", ["ID of the user to remove"], true, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_id, "<group-rtid>"),
                    new CodeSampleArgument(_userId, "<user-id>"),
                ],
                    description: "Basic usage"),
            ]
        );

    public override async Task Execute()
    {
        var rtId = OctoObjectId.Parse(CommandArgumentValue.GetArgumentScalarValue<string>(_id));
        var userId = CommandArgumentValue.GetArgumentScalarValue<string>(_userId);

        Logger.LogInformation("Removing user '{UserId}' from group '{RtId}' at '{ServiceClientServiceUri}'",
            userId, rtId, ServiceClient.ServiceUri);

        await _methods.RunAsync(RtGroupMethodClient.RemoveMemberMethodId,
            () => new RtGroupMethodClient(_methods.TenantClient).RemoveMemberAsync(rtId.ToString(), RtGroupMemberKindEnum.User, userId),
            treatAsSuccess: e => e.Code is RtGroupMethodClient.RemoveMemberErrors.MemberNotInGroup
                or RtGroupMethodClient.RemoveMemberErrors.MemberNotFound);

        Logger.LogInformation("User '{UserId}' removed from group '{RtId}'", userId, rtId);
    }
}
