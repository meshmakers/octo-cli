using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Groups;

internal class CreateGroup : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _description;
    private readonly IArgument _name;
    private readonly IArgument _roleIds;

    public CreateGroup(ILogger<CreateGroup> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "CreateGroup", "Creates a group.", options,
            identityServicesClient, authenticationService)
    {
        _name = CommandArgumentValue.AddArgument("n", "name", ["Name of the group"], true, 1);
        _description = CommandArgumentValue.AddArgument("d", "description",
            ["Optional description of the group"], false, 1);
        _roleIds = CommandArgumentValue.AddArgument("rids", "roleIds",
            ["Comma-separated list of role IDs to assign"], false, 1);
    }

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_name);

        Logger.LogInformation("Creating group '{Name}' at '{ServiceClientServiceUri}'",
            name, ServiceClient.ServiceUri);

        var dto = new CreateGroupDto
        {
            GroupName = name,
            GroupDescription = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_description),
            RoleIds = ParseCommaSeparatedList(_roleIds)
        };
        await ServiceClient.CreateGroup(dto);

        Logger.LogInformation("Group '{Name}' created", name);
    }

    private List<string>? ParseCommaSeparatedList(IArgument argument)
    {
        if (!CommandArgumentValue.IsArgumentUsed(argument))
        {
            return null;
        }

        var value = CommandArgumentValue.GetArgumentScalarValue<string>(argument);
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
}
