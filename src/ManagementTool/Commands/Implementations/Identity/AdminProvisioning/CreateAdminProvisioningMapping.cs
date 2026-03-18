using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.AdminProvisioning;

internal class CreateAdminProvisioningMapping : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _roleIds;
    private readonly IArgument _sourceTenantId;
    private readonly IArgument _sourceUserId;
    private readonly IArgument _sourceUserName;
    private readonly IArgument _targetTenantId;

    public CreateAdminProvisioningMapping(ILogger<CreateAdminProvisioningMapping> logger,
        IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "CreateAdminProvisioningMapping",
            "Creates an admin provisioning mapping in a target tenant.", options, identityServicesClient,
            authenticationService)
    {
        _targetTenantId = CommandArgumentValue.AddArgument("ttid", "targetTenantId",
            ["Target tenant ID"], true, 1);
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
        var targetTenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_targetTenantId);
        var sourceUserName = CommandArgumentValue.GetArgumentScalarValue<string>(_sourceUserName);

        Logger.LogInformation(
            "Creating admin provisioning mapping for '{SourceUserName}' in target tenant '{TargetTenantId}' at '{ServiceClientServiceUri}'",
            sourceUserName, targetTenantId, ServiceClient.ServiceUri);

        List<string>? roleIds = null;
        if (CommandArgumentValue.IsArgumentUsed(_roleIds))
        {
            var roleIdsValue = CommandArgumentValue.GetArgumentScalarValue<string>(_roleIds);
            roleIds = roleIdsValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        var dto = new CreateExternalTenantUserMappingDto
        {
            SourceTenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_sourceTenantId),
            SourceUserId = CommandArgumentValue.GetArgumentScalarValue<string>(_sourceUserId),
            SourceUserName = sourceUserName,
            RoleIds = roleIds
        };
        await ServiceClient.CreateAdminProvisioningMapping(targetTenantId, dto);

        Logger.LogInformation("Admin provisioning mapping for '{SourceUserName}' created in target tenant '{TargetTenantId}'",
            sourceUserName, targetTenantId);
    }
}
