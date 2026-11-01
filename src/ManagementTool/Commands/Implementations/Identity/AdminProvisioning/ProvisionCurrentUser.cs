using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.AdminProvisioning;

internal class ProvisionCurrentUser : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _targetTenantId;

    public ProvisionCurrentUser(ILogger<ProvisionCurrentUser> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "ProvisionCurrentUser",
            "Provisions the current user in a target tenant.", options, identityServicesClient,
            authenticationService)
    {
        _targetTenantId = CommandArgumentValue.AddArgument("ttid", "targetTenantId",
            ["Target tenant ID"], true, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_targetTenantId, "customer-project")], description: "Provision yourself in a target tenant with all available roles and TenantOwners group membership"),
            ],
            Notes:
            [
                "All admin provisioning commands must be run from the **system tenant context**.",
                "Run from a context of an intermediate tenant (a cross-tenant user), the mapping is written for your home identity, so every context of yours reaches the target tenant; the output names the source tenant and user the mapping was written for.",
            ]
        );

    public override async Task Execute()
    {
        var targetTenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_targetTenantId);

        Logger.LogInformation(
            "Provisioning current user in target tenant '{TargetTenantId}' at '{ServiceClientServiceUri}'",
            targetTenantId, ServiceClient.ServiceUri);

        var mapping = await ServiceClient.ProvisionCurrentUser(targetTenantId);

        LogProvisioned(Logger, targetTenantId, mapping);
    }

    /// <summary>
    ///     Names the identity the mapping was written for (AB#8017): from a context scoped to an intermediate tenant the
    ///     identity service provisions the home identity of the shadow user, not the shadow user itself.
    /// </summary>
    internal static void LogProvisioned(ILogger logger, string targetTenantId, ExternalTenantUserMappingDto? mapping)
    {
        if (mapping == null || string.IsNullOrEmpty(mapping.SourceTenantId))
        {
            logger.LogInformation("Current user provisioned in target tenant '{TargetTenantId}'", targetTenantId);
            return;
        }

        logger.LogInformation(
            "Current user provisioned in target tenant '{TargetTenantId}': mapping written for user '{SourceUserName}' ({SourceUserId}) of tenant '{SourceTenantId}'",
            targetTenantId, mapping.SourceUserName, mapping.SourceUserId, mapping.SourceTenantId);
    }
}
