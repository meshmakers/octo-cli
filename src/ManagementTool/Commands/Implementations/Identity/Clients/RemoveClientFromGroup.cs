using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;

internal class RemoveClientFromGroup : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IdentityMethodCall _methods;
    private readonly IArgument _id;
    private readonly IArgument _clientId;

    public RemoveClientFromGroup(ILogger<RemoveClientFromGroup> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "RemoveClientFromGroup", "Removes a client from a group.",
            options, identityServicesClient, authenticationService)
    {
        _methods = new IdentityMethodCall(tenantClient, authenticationService, logger);
        _id = CommandArgumentValue.AddArgument("id", "identifier", ["ID of the group"], true, 1);
        _clientId = CommandArgumentValue.AddArgument("cid", "clientId", ["ID of the client to remove"], true, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_id, "<group-rtid>"),
                    new CodeSampleArgument(_clientId, "ci-deploy"),
                ],
                    description: "Basic usage"),
            ]
        );

    public override async Task Execute()
    {
        var rtId = OctoObjectId.Parse(CommandArgumentValue.GetArgumentScalarValue<string>(_id));
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientId);

        Logger.LogInformation("Removing client '{ClientId}' from group '{RtId}' at '{ServiceClientServiceUri}'",
            clientId, rtId, ServiceClient.ServiceUri);

        await _methods.RunAsync(RtGroupMethodClient.RemoveMemberMethodId,
            () => new RtGroupMethodClient(_methods.TenantClient).RemoveMemberAsync(rtId.ToString(),
                RtGroupMemberKindEnum.Client, clientId),
            // As the REST endpoint: a runtime id that names no client and no member has nothing to remove.
            treatAsSuccess: e => e.Code == RtGroupMethodClient.RemoveMemberErrors.MemberNotInGroup ||
                                 (e.Code == RtGroupMethodClient.RemoveMemberErrors.MemberNotFound &&
                                  OctoObjectId.TryParse(clientId.Trim(), out _)));

        Logger.LogInformation("Client '{ClientId}' removed from group '{RtId}'", clientId, rtId);
    }
}
