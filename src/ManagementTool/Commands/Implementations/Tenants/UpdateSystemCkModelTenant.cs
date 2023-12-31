using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Tenants;

internal class UpdateSystemCkModelTenant : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IArgument _tenantIdArg;

    public UpdateSystemCkModelTenant(ILogger<UpdateSystemCkModelTenant> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IAuthenticationService authenticationServices)
        : base(logger, "UpdateSystemCkModel",
            "Updates the system construction kit model of a tenant to the latest version.", options, assetServicesClient,
            authenticationServices)
    {
        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId", new[] { "Id of tenant" },
            true, 1);
    }

    public override async Task Execute()
    {
        var tenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg).ToLower();

        Logger.LogInformation("Updating tenant \'{TenantId}\' on at \'{ServiceClientServiceUri}\'", tenantId,
            ServiceClient.ServiceUri);

        await ServiceClient.UpdateSystemCkModelOfTenant(tenantId);

        Logger.LogInformation("Tenant \'{TenantId}\' on at \'{ServiceClientServiceUri}\' updated", tenantId,
            ServiceClient.ServiceUri);
    }
}
