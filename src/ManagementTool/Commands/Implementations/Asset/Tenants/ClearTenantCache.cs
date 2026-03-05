using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;

internal class ClearTenantCache : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _tenantIdArg;
    private readonly IArgument _yesArg;

    public ClearTenantCache(ILogger<ClearTenantCache> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "ClearCache", "Clears the cache of a tenant", options,
            assetServicesClient, authenticationService)
    {
        _confirmationService = confirmationService;

        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId", ["Id of tenant"],
            true, 1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
    }

    public override async Task Execute()
    {
        var tenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg).ToLower();

        if (!CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm($"Are you sure you want to clear the cache for tenant '{tenantId}'?"))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation("Clearing cache tenant \'{TenantId}\' on at \'{ServiceClientServiceUri}\'", tenantId,
            ServiceClient.ServiceUri);

        await ServiceClient.ClearTenantCacheAsync(tenantId);

        Logger.LogInformation("Tenant cache \'{TenantId}\' on at \'{ServiceClientServiceUri}\' cleared", tenantId,
            ServiceClient.ServiceUri);
    }
}
