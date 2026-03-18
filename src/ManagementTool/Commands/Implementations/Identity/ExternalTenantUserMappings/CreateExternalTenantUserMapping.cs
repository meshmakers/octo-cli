using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.ExternalTenantUserMappings;

internal class CreateExternalTenantUserMapping : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _roleIds;
    private readonly IArgument _sourceTenantId;
    private readonly IArgument _sourceUserId;
    private readonly IArgument _sourceUserName;

    public CreateExternalTenantUserMapping(ILogger<CreateExternalTenantUserMapping> logger,
        IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "CreateExternalTenantUserMapping",
            "Creates an external tenant user mapping.", options, identityServicesClient, authenticationService)
    {
        _sourceTenantId = CommandArgumentValue.AddArgument("stid", "sourceTenantId",
            ["Source tenant ID"], true, 1);
        _sourceUserId = CommandArgumentValue.AddArgument("suid", "sourceUserId",
            ["Source user ID"], true, 1);
        _sourceUserName = CommandArgumentValue.AddArgument("sun", "sourceUserName",
            ["Source user name"], true, 1);
        _roleIds = CommandArgumentValue.AddArgument("rids", "roleIds",
            ["Comma-separated list of role IDs to assign"], false, 1);
    }

    public override async Task Execute()
    {
        var sourceTenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_sourceTenantId);
        var sourceUserName = CommandArgumentValue.GetArgumentScalarValue<string>(_sourceUserName);

        Logger.LogInformation(
            "Creating external tenant user mapping for '{SourceUserName}' from tenant '{SourceTenantId}' at '{ServiceClientServiceUri}'",
            sourceUserName, sourceTenantId, ServiceClient.ServiceUri);

        List<string>? roleIds = null;
        if (CommandArgumentValue.IsArgumentUsed(_roleIds))
        {
            var roleIdsValue = CommandArgumentValue.GetArgumentScalarValue<string>(_roleIds);
            roleIds = roleIdsValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        var dto = new CreateExternalTenantUserMappingDto
        {
            SourceTenantId = sourceTenantId,
            SourceUserId = CommandArgumentValue.GetArgumentScalarValue<string>(_sourceUserId),
            SourceUserName = sourceUserName,
            RoleIds = roleIds
        };
        await ServiceClient.CreateExternalTenantUserMapping(dto);

        Logger.LogInformation("External tenant user mapping for '{SourceUserName}' created", sourceUserName);
    }
}
