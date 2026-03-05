using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.AdminProvisioning;

internal class GetAdminProvisioningMappings : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _targetTenantId;

    public GetAdminProvisioningMappings(ILogger<GetAdminProvisioningMappings> logger,
        IOptions<OctoToolOptions> options,
        IConsoleService consoleService,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "GetAdminProvisioningMappings",
            "Gets admin provisioning mappings for a target tenant.", options, identityServicesClient,
            authenticationService)
    {
        _consoleService = consoleService;
        _targetTenantId = CommandArgumentValue.AddArgument("ttid", "targetTenantId",
            ["Target tenant ID"], true, 1);
    }

    public override async Task Execute()
    {
        var targetTenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_targetTenantId);

        Logger.LogInformation(
            "Getting admin provisioning mappings for target tenant '{TargetTenantId}' from '{ServiceClientServiceUri}'",
            targetTenantId, ServiceClient.ServiceUri);

        var result = await ServiceClient.GetAdminProvisioningMappings(targetTenantId);
        if (!result.Any())
        {
            Logger.LogInformation("No admin provisioning mappings have been returned");
            return;
        }

        var resultString = JsonConvert.SerializeObject(result, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }
}
