using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AiServices;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.Methods;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;

internal class CreateTenant : ServiceClientOctoCommand<IAssetServicesClient>
{
    /// <summary>
    ///     Blueprint name (without version) of the starter tenant (AB#8452). Creating it from the system tenant gives the
    ///     new tenant the cluster's default monthly LLM cap, as MCP <c>create_tenant starter=true</c> does.
    /// </summary>
    internal const string StarterBlueprintName = "System.Ai.Starter";

    private readonly IAiServicesClient _aiServicesClient;
    private readonly IAuthenticationService _authenticationService;
    private readonly IOptions<OctoToolAuthenticationOptions> _authenticationOptions;
    private readonly IdentityMethodCall _methods;
    private readonly IArgument _databaseArg;
    private readonly IArgument _tenantIdArg;
    private readonly IArgument _noProvisionArg;
    private readonly IArgument _scratchArg;
    private readonly IArgument _blueprintArg;

    public CreateTenant(ILogger<CreateTenant> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, ITenantClient tenantClient,
        IAiServicesClient aiServicesClient, IAuthenticationService authenticationService,
        IOptions<OctoToolAuthenticationOptions> authenticationOptions)
        : base(logger, Constants.AssetRepositoryServicesGroup, "Create",
            "Creates a new tenant and provisions the current user as admin.", options,
            assetServicesClient, authenticationService)
    {
        // The provisioning runs the System.Identity method ExternalTenantUserMapping.ProvisionCurrentUser in the current
        // (parent) tenant, like the ProvisionCurrentUser command; the REST method of the SDK is obsolete (AB#9044).
        _methods = new IdentityMethodCall(tenantClient, authenticationService, logger);
        _aiServicesClient = aiServicesClient;
        _authenticationService = authenticationService;
        _authenticationOptions = authenticationOptions;

        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId", ["Id of tenant"],
            true, 1);
        _databaseArg = CommandArgumentValue.AddArgument("db", "database",
            ["Name of database (optional: without it the service names the database after the tenant id, with the " +
             "instance's tenant-database prefix when one is configured)"], false, 1);
        _noProvisionArg = CommandArgumentValue.AddArgument("np", "no-provision",
            ["Skip admin provisioning of the current user"], false, 0);
        _scratchArg = CommandArgumentValue.AddArgument("sc", "scratch",
            ["Create a scratch tenant: the client-credentials client that runs this command becomes administrator " +
             "of the new tenant (no human login needed)"], false, 0);
        _blueprintArg = CommandArgumentValue.AddArgument("b", "blueprint",
            ["Catalog blueprint to apply while creating the tenant (<Name>-<version>, e.g. MyBlueprint-1.0.0). " +
             "Transactional: if the blueprint is unknown or fails to apply, the tenant is not created"], false, 1);
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
                    new CodeSampleArgument(_tenantIdArg, "newtenant"),
                ],
                    description: "Default database name (tenant id, prefixed with the instance's tenant-database prefix)"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_tenantIdArg, "scratch1"),
                    new CodeSampleArgument(_databaseArg, "scratch1_db"),
                    new CodeSampleArgument(_scratchArg),
                ],
                    description: "Scratch tenant for an agent (client-credentials login of the parent tenant)"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_tenantIdArg, "newtenant"),
                    new CodeSampleArgument(_blueprintArg, "MyBlueprint-1.0.0"),
                ],
                    description: "Tenant with a catalog blueprint applied in the same call"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_tenantIdArg, "starter1"),
                    new CodeSampleArgument(_blueprintArg, "System.Ai.Starter-1.2.0"),
                ],
                    description: "Starter tenant from the system tenant: blueprint applied and the default LLM cap set"),
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
                "--database is optional (AB#8797): without it the service names the database after the tenant id. An instance with " +
                "a tenant-database prefix (System:TenantDatabasePrefix) prefixes that name and refuses an explicit name without " +
                "the prefix (HTTP 409, 'Database name ... is not available'). Services before AB#8797 require --database.",
                "--blueprint applies a catalog blueprint (ListBlueprints shows the ids) while the tenant is created, in " +
                "one transaction: when the blueprint is unknown or fails to apply, the service aborts, no tenant is left behind " +
                "and the command ends with a non-zero exit code and the service's message ('Blueprint application failed: ...'). " +
                "Combines with --database, --scratch and --no-provision; the current user is provisioned only after the tenant " +
                "and its blueprint exist. Same result as Create followed by InstallBlueprint, without a half-set-up tenant in " +
                "between.",
                "Starter tenant (AB#8452): when the context's tenant is the system tenant and the blueprint is " +
                $"{StarterBlueprintName}-<version>, the command then gives the new tenant the cluster's default monthly LLM " +
                "cap on the central key (AI service, POST system/v1/aiSpendCaps/<tenant>/starterDefault), as MCP " +
                "create_tenant starter=true does. The cluster configuration decides the amount; an existing cap is never " +
                "changed, and a cluster without a configured default writes nothing (a warning says so). The context needs " +
                "the AI service URL; without it the command stops before the tenant is created. Other blueprints and other " +
                "parent tenants set no cap. The endpoint takes only a person login (device code) of the system tenant with " +
                "TenantManagement: in a client-credentials session (e.g. --scratch) the command does not call it and warns, " +
                "naming SetAiSpendCap. When the cap call fails, the tenant exists: the error names SetAiSpendCap as the " +
                "remedy and the command ends with a non-zero exit code.",
            ]
        );

    public override async Task Execute()
    {
        var tenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg).ToLower();
        // AB#8797: without --database the service derives the name (tenant id, with the instance's prefix).
        var databaseName = CommandArgumentValue.IsArgumentUsed(_databaseArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_databaseArg).ToLower()
            : null;
        var scratch = CommandArgumentValue.IsArgumentUsed(_scratchArg);
        // The service applies the blueprint in the create transaction; a failure aborts the create.
        var blueprintId = CommandArgumentValue.IsArgumentUsed(_blueprintArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_blueprintArg).Trim()
            : null;
        if (blueprintId is { Length: 0 })
        {
            throw new ArgumentException("--blueprint needs a blueprint id (<Name>-<version>).");
        }
        // A scratch tenant is created by a service token: there is no user to provision, the creator's mirror is the admin.
        var skipProvisioning = scratch || CommandArgumentValue.IsArgumentUsed(_noProvisionArg);
        // AB#8452: the platform operator's starter path also sets the default LLM cap, as MCP create_tenant starter=true.
        var applyStarterCap = blueprintId != null && IsStarterBlueprint(blueprintId) &&
                              SystemTenants.IsSystemTenant(Options.Value.TenantId);
        // A client-credentials session skips the cap call (warning below), so it needs no AI service URL.
        if (applyStarterCap && !IsServiceSession() && string.IsNullOrWhiteSpace(Options.Value.AiServiceUrl))
        {
            // Fail before anything is created: otherwise the tenant would exist without its cap.
            throw new ToolException(
                $"Creating a {StarterBlueprintName} tenant from the system tenant sets its default LLM cap on the AI " +
                "service, but the context has no AI service URL. Add it to the context, or create the tenant without " +
                "--blueprint and install the blueprint afterwards.");
        }

        Logger.LogInformation(
            "Creating {Kind} tenant \'{TenantId}\' (database \'{DatabaseName}\') at \'{ServiceClientServiceUri}\'",
            scratch ? "scratch" : "a", tenantId, databaseName ?? "<default>", ServiceClient.ServiceUri);

        if (blueprintId != null)
        {
            Logger.LogInformation("Applying blueprint '{BlueprintId}' in the same transaction", blueprintId);
            await ServiceClient.CreateTenantAsync(tenantId, databaseName, scratch, blueprintId);
        }
        else if (scratch)
        {
            await ServiceClient.CreateTenantAsync(tenantId, databaseName, scratch: true);
        }
        else
        {
            await ServiceClient.CreateTenantAsync(tenantId, databaseName);
        }

        Logger.LogInformation(
            "Tenant \'{TenantId}\' (database \'{DatabaseName}\') at \'{ServiceClientServiceUri}\' created", tenantId,
            databaseName ?? "<default>", ServiceClient.ServiceUri);

        if (applyStarterCap && IsServiceSession())
        {
            // The endpoint refuses service tokens (PersonTokenRequirement): a mirrored client's credentials are also
            // its parent's, so a client token of the system tenant does not prove who calls.
            Logger.LogWarning(
                "Tenant '{TenantId}' was created without its default LLM cap: the starter cap needs a person login of " +
                "the system tenant, this is a client-credentials session. A platform admin sets it with 'SetAiSpendCap -t " +
                "{TenantId} -c <EUR> -ks CentralKey -r <reason>'", tenantId, tenantId);
        }
        else if (applyStarterCap)
        {
            await ApplyStarterDefaultCapAsync(tenantId, skipProvisioning);
        }

        if (!skipProvisioning)
        {
            Logger.LogInformation(
                "Provisioning current user as admin in tenant '{TenantId}' via the method gateway at '{TenantServiceUri}'",
                tenantId, _methods.TenantClient.ServiceUri);

            RtAdminProvisioningResultRecordDto? record;
            try
            {
                record = await _methods.RunAsync(RtExternalTenantUserMappingMethodClient.ProvisionCurrentUserMethodId,
                    () => new RtExternalTenantUserMappingMethodClient(_methods.TenantClient).ProvisionCurrentUserAsync(
                        tenantId));
            }
            catch (Exception e) when (e is ServiceClientException or CkMethodCallException)
            {
                // The tenant exists at this point; the error of the method call alone would read as if nothing changed.
                // Exception and exit code stay those of the method call.
                Logger.LogError(
                    "Tenant '{TenantId}' was created, but provisioning the current user as its admin failed. Fix the " +
                    "cause below, then run 'ProvisionCurrentUser -ttid {TenantId}'", tenantId, tenantId);
                throw;
            }

            var mapping = IdentityMethodCall.ToMappingDto(record);

            Implementations.Identity.AdminProvisioning.ProvisionCurrentUser.LogProvisioned(Logger, tenantId, mapping);
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

    /// <summary>
    ///     A client-credentials session has no refresh token (the convention of <see cref="ChildTenantAccess" />); a
    ///     person login (device code) has one.
    /// </summary>
    private bool IsServiceSession() => string.IsNullOrEmpty(_authenticationOptions.Value.RefreshToken);

    /// <summary>True for <c>System.Ai.Starter-&lt;version&gt;</c> (name compared without case).</summary>
    internal static bool IsStarterBlueprint(string blueprintId) =>
        blueprintId.StartsWith(StarterBlueprintName + "-", StringComparison.OrdinalIgnoreCase) &&
        blueprintId.Length > StarterBlueprintName.Length + 1;

    private async Task ApplyStarterDefaultCapAsync(string tenantId, bool skipProvisioning)
    {
        Logger.LogInformation("Setting the default LLM cap of starter tenant '{TenantId}' at '{AiServiceUri}'", tenantId,
            _aiServicesClient.ServiceUri);

        StarterSpendCapResultDto result;
        try
        {
            await _authenticationService.EnsureAuthenticated(_aiServicesClient.AccessToken);
            result = await _aiServicesClient.ApplyStarterDefaultSpendCapAsync(tenantId);
        }
        catch (Exception e) when (e is ServiceClientException or ServiceClientResultException or HttpRequestException)
        {
            // The tenant and its blueprint exist; say so, and name the remedy. Exception and exit code stay the call's.
            Logger.LogError(
                "Tenant '{TenantId}' was created, but setting its default LLM cap failed; it stays without central-key " +
                "spend until a cap is set. Fix the cause below, then run 'SetAiSpendCap -t {TenantId} -c <EUR> -ks " +
                "CentralKey -r <reason>' as platform admin{Provisioning}", tenantId, tenantId,
                skipProvisioning ? "" : $", and 'ProvisionCurrentUser -ttid {tenantId}' (provisioning was not run)");
            throw;
        }

        switch (result.Outcome)
        {
            case StarterSpendCapResultDto.Created:
                Logger.LogInformation("Default LLM cap of tenant '{TenantId}' set: {KeySource}, {Cap} EUR per month",
                    tenantId, result.KeySource, result.CapMonthlyEur);
                break;
            case StarterSpendCapResultDto.Exists:
                Logger.LogInformation(
                    "Tenant '{TenantId}' already has an LLM cap ({KeySource}, {Cap} EUR per month); it was not changed",
                    tenantId, result.KeySource, result.CapMonthlyEur);
                break;
            default:
                Logger.LogWarning("No default LLM cap set for tenant '{TenantId}': {Message}", tenantId, result.Message);
                break;
        }
    }
}