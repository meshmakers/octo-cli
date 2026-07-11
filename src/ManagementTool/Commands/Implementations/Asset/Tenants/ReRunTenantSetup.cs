using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;

internal class ReRunTenantSetup : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _tenantIdArg;

    public ReRunTenantSetup(ILogger<ReRunTenantSetup> logger,
        IConsoleService consoleService,
        IOptions<OctoToolOptions> options, IAssetServicesClient assetServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "ReRunTenantSetup",
            "Re-opens a tenant's provisioning so the background reconciler completes it.", options,
            assetServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId", ["Id of tenant"], true, 1);
    }

    public override async Task Execute()
    {
        var tenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg).ToLower();

        var result = await ServiceClient.ReRunTenantSetupAsync(tenantId);
        if (result == null)
        {
            Logger.LogWarning("No lifecycle record found for tenant '{TenantId}'; nothing to re-run.", tenantId);
            return;
        }

        Logger.LogInformation("Tenant '{TenantId}' re-queued for setup (state: {State}).", tenantId, result.State);

        var resultString = JsonConvert.SerializeObject(result, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }
}
