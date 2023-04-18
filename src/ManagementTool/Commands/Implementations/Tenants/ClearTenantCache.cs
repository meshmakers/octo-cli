using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.Client.System;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Tenants;

internal class ClearTenantCache : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IArgument _tenantIdArg;

    public ClearTenantCache(ILogger<ClearTenantCache> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IAuthenticationService authenticationService)
        : base(logger, "ClearCache", "Clears the cache of a tenant", options, assetServicesClient, authenticationService)
    {
        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId", new[] { "Id of tenant" },
            true, 1);
    }

    public override async Task Execute()
    {
        var tenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg).ToLower();

        Logger.LogInformation("Clearing cache tenant \'{TenantId}\' on at \'{ServiceClientServiceUri}\'", tenantId,
            ServiceClient.ServiceUri);

        await ServiceClient.ClearTenantCache(tenantId);

        Logger.LogInformation("Tenant cache \'{TenantId}\' on at \'{ServiceClientServiceUri}\' cleared", tenantId,
            ServiceClient.ServiceUri);
    }
}
