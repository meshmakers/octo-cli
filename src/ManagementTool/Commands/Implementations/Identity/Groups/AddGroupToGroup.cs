using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Groups;

internal class AddGroupToGroup : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IdentityMethodCall _methods;
    private readonly IArgument _childGroupId;
    private readonly IArgument _id;

    public AddGroupToGroup(ILogger<AddGroupToGroup> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "AddGroupToGroup",
            "Adds a child group to a parent group.", options, identityServicesClient, authenticationService)
    {
        _methods = new IdentityMethodCall(tenantClient, authenticationService, logger);
        _id = CommandArgumentValue.AddArgument("id", "identifier", ["ID of the parent group"], true, 1);
        _childGroupId = CommandArgumentValue.AddArgument("cgid", "childGroupId",
            ["ID of the child group to add"], true, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_id, "<parent-group-rtid>"),
                    new CodeSampleArgument(_childGroupId, "<child-group-rtid>"),
                ],
                    description: "Basic usage"),
            ]
        );

    public override async Task Execute()
    {
        var rtId = OctoObjectId.Parse(CommandArgumentValue.GetArgumentScalarValue<string>(_id));
        var childGroupId = CommandArgumentValue.GetArgumentScalarValue<string>(_childGroupId);

        Logger.LogInformation(
            "Adding child group '{ChildGroupId}' to group '{RtId}' at '{ServiceClientServiceUri}'",
            childGroupId, rtId, ServiceClient.ServiceUri);

        await _methods.RunAsync(RtGroupMethodClient.AddChildGroupMethodId,
            () => new RtGroupMethodClient(_methods.TenantClient).AddChildGroupAsync(rtId.ToString(), childGroupId),
            treatAsSuccess: e => e.Code == RtGroupMethodClient.AddChildGroupErrors.ChildGroupAlreadyInGroup);

        Logger.LogInformation("Child group '{ChildGroupId}' added to group '{RtId}'", childGroupId, rtId);
    }
}
