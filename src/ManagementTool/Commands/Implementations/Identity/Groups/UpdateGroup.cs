using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Groups;

internal class UpdateGroup : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IdentityMethodCall _methods;
    private readonly IArgument _description;
    private readonly IArgument _id;
    private readonly IArgument _name;

    public UpdateGroup(ILogger<UpdateGroup> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "UpdateGroup", "Updates a group.", options,
            identityServicesClient, authenticationService)
    {
        _methods = new IdentityMethodCall(tenantClient, authenticationService, logger);
        _id = CommandArgumentValue.AddArgument("id", "identifier", ["ID of the group"], true, 1);
        _name = CommandArgumentValue.AddArgument("n", "name", ["Name of the group"], true, 1);
        _description = CommandArgumentValue.AddArgument("d", "description",
            ["Optional description of the group"], false, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_id, "<group-rtid>"),
                    new CodeSampleArgument(_name, "New Name"),
                    new CodeSampleArgument(_description, "New description"),
                ],
                    description: "Basic usage"),
            ]
        );

    public override async Task Execute()
    {
        var rtId = OctoObjectId.Parse(CommandArgumentValue.GetArgumentScalarValue<string>(_id));

        Logger.LogInformation("Updating group '{RtId}' at '{ServiceClientServiceUri}'",
            rtId, ServiceClient.ServiceUri);

        var dto = new UpdateGroupDto
        {
            GroupName = CommandArgumentValue.GetArgumentScalarValue<string>(_name),
            GroupDescription = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_description)
        };
        await _methods.RunAsync(RtGroupMethodClient.UpdateGroupMethodId,
            () => new RtGroupMethodClient(_methods.TenantClient).UpdateGroupAsync(rtId.ToString(), dto.GroupName,
                dto.GroupDescription));

        Logger.LogInformation("Group '{RtId}' updated", rtId);
    }
}
