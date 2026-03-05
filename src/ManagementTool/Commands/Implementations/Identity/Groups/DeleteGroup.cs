using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Groups;

internal class DeleteGroup : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _id;

    public DeleteGroup(ILogger<DeleteGroup> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "DeleteGroup", "Deletes a group.", options,
            identityServicesClient, authenticationService)
    {
        _id = CommandArgumentValue.AddArgument("id", "identifier", ["ID of the group"], true, 1);
    }

    public override async Task Execute()
    {
        var rtId = CommandArgumentValue.GetArgumentScalarValue<OctoObjectId>(_id);

        Logger.LogInformation("Deleting group '{RtId}' from '{ServiceClientServiceUri}'",
            rtId, ServiceClient.ServiceUri);

        await ServiceClient.DeleteGroup(rtId);

        Logger.LogInformation("Group '{RtId}' deleted", rtId);
    }
}
