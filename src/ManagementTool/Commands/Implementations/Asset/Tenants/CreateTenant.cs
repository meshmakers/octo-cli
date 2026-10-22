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
    private readonly IArgument _scratchArg;

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
        _scratchArg = CommandArgumentValue.AddArgument("sc", "scratch",
            ["Create a scratch tenant: the client-credentials client that runs this command becomes administrator " +
             "of the new tenant (no human login needed)"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_tenantIdArg, "newtenant"),
                    new CodeSampleArgument(_databaseArg, "newtenant_db"),
                ],
                    description: "Basic usage"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_tenantIdArg, "scratch1"),
                    new CodeSampleArgument(_databaseArg, "scratch1_db"),
                    new CodeSampleArgument(_scratchArg),
                ],
                    description: "Scratch tenant for an agent (client-credentials login of the parent tenant)"),
            ],
            Notes:
            [
                "--scratch (AB#7192) is for agents and automation that log in with a client (LogInClientCredentials): the tenant " +
                "records the creating client, and that client's mirror in the new tenant becomes administrator (TenantOwners) of it. " +
                "The agent then gets a token for the child (client_credentials, acr_values tenant:<child> - octo-cli does this for " +
                "Delete --disable-features), works with the existing verbs, switches the capabilities off and deletes the tenant, " +
                "without any human login. The grant survives an identity restart and is revoked by removing the client from " +
                "TenantOwners (RemoveClientFromGroup).",
                "Accepted only for a client-credentials token of the direct parent with the role TenantManagement (HTTP 403 for a user " +
                "token or another tenant). The client has to be flagged AutoProvisionInChildTenants (AddClientCredentialsClient -apic) - " +
                "otherwise it has no mirror in the child and gets nothing. --scratch skips the provisioning of the current user (a " +
                "service token has no user). GetTenants shows isScratch, scratchCreatorClientId and scratchAccessRevoked.",
                "Without --scratch nothing changes: the current user becomes administrator unless --no-provision is given.",
            ]
        );

    public override async Task Execute()
    {
        var tenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg).ToLower();
        var databaseName = CommandArgumentValue.GetArgumentScalarValue<string>(_databaseArg).ToLower();
        var scratch = CommandArgumentValue.IsArgumentUsed(_scratchArg);
        // A scratch tenant is created by a service token: there is no user to provision, the creator's mirror is the admin.
        var skipProvisioning = scratch || CommandArgumentValue.IsArgumentUsed(_noProvisionArg);

        Logger.LogInformation(
            "Creating {Kind} tenant \'{TenantId}\' (database \'{DatabaseName}\') at \'{ServiceClientServiceUri}\'",
            scratch ? "scratch" : "a", tenantId, databaseName, ServiceClient.ServiceUri);

        if (scratch)
        {
            await ServiceClient.CreateTenantAsync(tenantId, databaseName, scratch: true);
        }
        else
        {
            await ServiceClient.CreateTenantAsync(tenantId, databaseName);
        }

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
        else if (scratch)
        {
            Logger.LogInformation(
                "Scratch tenant: the client that created it becomes administrator of tenant '{TenantId}' once identity has " +
                "set the tenant up (the client has to be flagged AutoProvisionInChildTenants)", tenantId);
        }
        else
        {
            Logger.LogInformation("Admin provisioning skipped (--no-provision)");
        }
    }
}