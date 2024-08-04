using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Tenants;

internal class CreateTenant : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IArgument _databaseArg;
    private readonly IArgument _tenantIdArg;

    public CreateTenant(ILogger<CreateTenant> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IAuthenticationService authenticationService)
        : base(logger, "Create", "Creates a new tenant.", options, assetServicesClient, authenticationService)
    {
        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId", ["Id of tenant"],
            true, 1);
        _databaseArg = CommandArgumentValue.AddArgument("db", "database", ["Name of database"], true,
            1);
    }

    public override async Task Execute()
    {
        var tenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg).ToLower();
        var databaseName = CommandArgumentValue.GetArgumentScalarValue<string>(_databaseArg).ToLower();

        Logger.LogInformation(
            "Creating tenant \'{TenantId}\' (database \'{DatabaseName}\') at \'{ServiceClientServiceUri}\'", tenantId,
            databaseName, ServiceClient.ServiceUri);

        await ServiceClient.CreateTenantAsync(tenantId, databaseName);

        Logger.LogInformation(
            "Tenant \'{TenantId}\' (database \'{DatabaseName}\') at \'{ServiceClientServiceUri}\' created", tenantId,
            databaseName, ServiceClient.ServiceUri);
    }
}
