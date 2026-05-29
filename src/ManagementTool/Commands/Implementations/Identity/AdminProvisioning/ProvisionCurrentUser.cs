using Meshmakers.Common.CommandLineParser;
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
            ]
        );

    public override async Task Execute()
    {
        var targetTenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_targetTenantId);

        Logger.LogInformation(
            "Provisioning current user in target tenant '{TargetTenantId}' at '{ServiceClientServiceUri}'",
            targetTenantId, ServiceClient.ServiceUri);

        await ServiceClient.ProvisionCurrentUser(targetTenantId);

        Logger.LogInformation("Current user provisioned in target tenant '{TargetTenantId}'", targetTenantId);
    }
}
