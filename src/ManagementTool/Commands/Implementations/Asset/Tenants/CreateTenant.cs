using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;

internal class CreateTenant : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IIdentityServicesClient _identityServicesClient;
    private readonly IArgument _databaseArg;
    private readonly IArgument _tenantIdArg;
    private readonly IArgument _noProvisionArg;

    public CreateTenant(ILogger<CreateTenant> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IIdentityServicesClient identityServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "Create",
            "Creates a new tenant and provisions the current user as admin.", options,
            assetServicesClient, authenticationService)
    {
        _identityServicesClient = identityServicesClient;

        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId", ["Id of tenant"],
            true, 1);
        _databaseArg = CommandArgumentValue.AddArgument("db", "database", ["Name of database"], true,
            1);
        _noProvisionArg = CommandArgumentValue.AddArgument("np", "no-provision",
            ["Skip admin provisioning of the current user"], false, 0);
    }

    public override async Task Execute()
    {
        var tenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg).ToLower();
        var databaseName = CommandArgumentValue.GetArgumentScalarValue<string>(_databaseArg).ToLower();
        var skipProvisioning = CommandArgumentValue.IsArgumentUsed(_noProvisionArg);

        Logger.LogInformation(
            "Creating tenant \'{TenantId}\' (database \'{DatabaseName}\') at \'{ServiceClientServiceUri}\'", tenantId,
            databaseName, ServiceClient.ServiceUri);

        await ServiceClient.CreateTenantAsync(tenantId, databaseName);

        Logger.LogInformation(
            "Tenant \'{TenantId}\' (database \'{DatabaseName}\') at \'{ServiceClientServiceUri}\' created", tenantId,
            databaseName, ServiceClient.ServiceUri);

        if (!skipProvisioning)
        {
            // Copy the access token from the asset services client to the identity services client
            _identityServicesClient.AccessToken.AccessToken = ServiceClient.AccessToken.AccessToken;

            Logger.LogInformation(
                "Provisioning current user as admin in tenant '{TenantId}' via identity service at '{IdentityServiceUri}'",
                tenantId, _identityServicesClient.ServiceUri);

            await _identityServicesClient.ProvisionCurrentUser(tenantId);

            Logger.LogInformation(
                "Current user provisioned as admin in tenant '{TenantId}'", tenantId);
        }
        else
        {
            Logger.LogInformation("Admin provisioning skipped (--no-provision)");
        }
    }
}