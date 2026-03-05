using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.AdminProvisioning;

internal class DeleteAdminProvisioningMapping : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _mappingId;
    private readonly IArgument _targetTenantId;

    public DeleteAdminProvisioningMapping(ILogger<DeleteAdminProvisioningMapping> logger,
        IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "DeleteAdminProvisioningMapping",
            "Deletes an admin provisioning mapping from a target tenant.", options, identityServicesClient,
            authenticationService)
    {
        _targetTenantId = CommandArgumentValue.AddArgument("ttid", "targetTenantId",
            ["Target tenant ID"], true, 1);
        _mappingId = CommandArgumentValue.AddArgument("mid", "mappingId",
            ["ID of the mapping to delete"], true, 1);
    }

    public override async Task Execute()
    {
        var targetTenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_targetTenantId);
        var mappingRtId = CommandArgumentValue.GetArgumentScalarValue<OctoObjectId>(_mappingId);

        Logger.LogInformation(
            "Deleting admin provisioning mapping '{MappingRtId}' from target tenant '{TargetTenantId}' at '{ServiceClientServiceUri}'",
            mappingRtId, targetTenantId, ServiceClient.ServiceUri);

        await ServiceClient.DeleteAdminProvisioningMapping(targetTenantId, mappingRtId);

        Logger.LogInformation("Admin provisioning mapping '{MappingRtId}' deleted from target tenant '{TargetTenantId}'",
            mappingRtId, targetTenantId);
    }
}
