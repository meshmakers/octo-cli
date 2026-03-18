using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Groups;

internal class UpdateGroup : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _description;
    private readonly IArgument _id;
    private readonly IArgument _name;

    public UpdateGroup(ILogger<UpdateGroup> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "UpdateGroup", "Updates a group.", options,
            identityServicesClient, authenticationService)
    {
        _id = CommandArgumentValue.AddArgument("id", "identifier", ["ID of the group"], true, 1);
        _name = CommandArgumentValue.AddArgument("n", "name", ["Name of the group"], true, 1);
        _description = CommandArgumentValue.AddArgument("d", "description",
            ["Optional description of the group"], false, 1);
    }

    public override async Task Execute()
    {
        var rtId = CommandArgumentValue.GetArgumentScalarValue<OctoObjectId>(_id);

        Logger.LogInformation("Updating group '{RtId}' at '{ServiceClientServiceUri}'",
            rtId, ServiceClient.ServiceUri);

        var dto = new UpdateGroupDto
        {
            GroupName = CommandArgumentValue.GetArgumentScalarValue<string>(_name),
            GroupDescription = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_description)
        };
        await ServiceClient.UpdateGroup(rtId, dto);

        Logger.LogInformation("Group '{RtId}' updated", rtId);
    }
}
