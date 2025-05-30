using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;

internal class CleanTenant : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IArgument _tenantIdArg;

    public CleanTenant(ILogger<CleanTenant> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IAuthenticationService authenticationServices)
        : base(logger, Constants.AssetRepositoryServicesGroup, "Clean",
            "Resets a tenant to factory defaults by deleting the construction kit and runtime model.", options,
            assetServicesClient, authenticationServices)
    {
        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId", ["Id of tenant"],
            true, 1);
    }

    public override async Task Execute()
    {
        var tenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg).ToLower();

        Logger.LogInformation("Cleaning tenant \'{TenantId}\' on at \'{ServiceClientServiceUri}\'", tenantId,
            ServiceClient.ServiceUri);

        await ServiceClient.CleanTenantAsync(tenantId);

        Logger.LogInformation("Tenant \'{TenantId}\' on at \'{ServiceClientServiceUri}\' cleaned", tenantId,
            ServiceClient.ServiceUri);
    }
}