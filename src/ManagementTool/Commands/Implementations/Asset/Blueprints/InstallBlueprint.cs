using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AiServices;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Blueprints;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.CkModelCatalog;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Blueprints;

internal class InstallBlueprint : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IAuthenticationService _authenticationService;
    private readonly IAiServicesClient? _aiServicesClient;
    private readonly IOptions<OctoToolAuthenticationOptions>? _authenticationOptions;
    private readonly IChildTenantAssetClientFactory? _childTenantAssetClientFactory;
    private readonly IArgument _blueprintIdArg;
    private readonly IArgument _childTenantIdArg;
    private readonly IArgument _forceArg;
    private readonly IArgument _forceBreakingUpdateArg;

    public InstallBlueprint(
        ILogger<InstallBlueprint> logger,
        IConsoleService consoleService,
        IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient,
        IAuthenticationService authenticationService,
        IAiServicesClient? aiServicesClient = null,
        IOptions<OctoToolAuthenticationOptions>? authenticationOptions = null,
        IChildTenantAssetClientFactory? childTenantAssetClientFactory = null)
        : base(logger, Constants.AssetRepositoryServicesGroup, "InstallBlueprint",
            "Installs a blueprint into the current tenant. CK models are loaded and seed data is imported via upsert. " +
            "Requires the tenant role DataModelManagement (TenantOwners carry it); otherwise 403 DataModelManagementRequired.",
            options, assetServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _authenticationService = authenticationService;
        _aiServicesClient = aiServicesClient;
        _authenticationOptions = authenticationOptions;
        _childTenantAssetClientFactory = childTenantAssetClientFactory;

        _blueprintIdArg = CommandArgumentValue.AddArgument("b", "blueprintId",
            ["Fully-qualified blueprint id, e.g. 'MyBlueprint-1.0.0'"], true, 1);

        _forceArg = CommandArgumentValue.AddArgument("f", "force",
            ["Re-apply seed data via upsert even if the same version is already recorded (recovery)"],
            false, 0);

        _forceBreakingUpdateArg = CommandArgumentValue.AddArgument("fbu", "forceBreakingUpdate",
            [
                "Install although the CK models of the blueprint would break an installed CK model (tenant upgrade guard).",
                "Without it the service refuses with CK_UPGRADE_BLOCKED before the first change. Needs the data model management role."
            ],
            false, 0);

        _childTenantIdArg = CommandArgumentValue.AddArgument("ctid", "child-tenant-id",
            ["ID of a child tenant of the context's tenant to install into instead of the context's tenant; your token " +
             "for that tenant is obtained as Delete --disable-features does"], false, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(
                    arguments: [new CodeSampleArgument(_blueprintIdArg, "Eda.Adapter-1.2.0")],
                    description: "Install a blueprint; refused when its CK models would break an installed model"),
                new CodeSample(
                    arguments:
                    [
                        new CodeSampleArgument(_blueprintIdArg, "Eda.Adapter-1.2.0"),
                        new CodeSampleArgument(_forceBreakingUpdateArg)
                    ],
                    description: "Install anyway; the broken models become ResolveFailed"),
                new CodeSample(
                    arguments:
                    [
                        new CodeSampleArgument(_blueprintIdArg, "System.Ai.Starter-1.2.0"),
                        new CodeSampleArgument(_childTenantIdArg, "starter1")
                    ],
                    description: "From the system tenant: install the starter into a child and set its default LLM cap"),
            ],
            Notes:
            [
                "The tenant upgrade guard judges the CK models the blueprint installs or raises as one set against the installed models (same rules and output as ImportFromCatalog).",
                "--force re-applies the seed data of an already installed version; it does not override the guard. Use --forceBreakingUpdate for that.",
                "A refused install changes nothing: no CK model, no installation record, no seed data.",
                "Archives the seed creates are activated and new rollups are backfilled; the output lists each archive (activated, skipped with a reason, failed) and the backfill job ids. On a tenant without stream data the install succeeds, the archives are left out and a warning says so.",
                "--child-tenant-id <tenant> installs into a child tenant (at any depth) of the context's tenant with your " +
                "token for that child (a user session exchanges the context's token, a client-credentials session uses the " +
                "client's mirror there, e.g. the creator of a scratch tenant).",
                "Starter tenant (AB#8452): with --child-tenant-id from a context of the system tenant and the blueprint " +
                $"{StarterDefaultCap.StarterBlueprintName}-<version>, a successful install then gives the child the " +
                "cluster's default monthly LLM cap on the central key (AI service, POST " +
                "system/v1/aiSpendCaps/<tenant>/starterDefault), as Create --blueprint does from the system tenant. An " +
                "existing cap is never changed, so a repeated install changes nothing. The endpoint takes only a person " +
                "login of the system tenant: in a client-credentials session the command does not call it and warns, " +
                "naming SetAiSpendCap. A person login needs the AI service URL in the context; without it the command " +
                "stops before the install. Other blueprints, other parent tenants and installs without --child-tenant-id " +
                "set no cap."
            ]);

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing - configure it via the active context");
            return;
        }

        var blueprintId = CommandArgumentValue.GetArgumentScalarValue<string>(_blueprintIdArg);
        var force = CommandArgumentValue.IsArgumentUsed(_forceArg);
        var forceBreakingUpdate = CommandArgumentValue.IsArgumentUsed(_forceBreakingUpdateArg);
        var childTenantId = CommandArgumentValue.IsArgumentUsed(_childTenantIdArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_childTenantIdArg)
            : null;

        // AB#8452: the starter path from the system tenant also sets the default LLM cap of the child.
        var applyStarterCap = childTenantId != null && StarterDefaultCap.IsStarterBlueprint(blueprintId) &&
                              SystemTenants.IsSystemTenant(Options.Value.TenantId);
        var serviceSession = _authenticationOptions == null ||
                             StarterDefaultCap.IsServiceSession(_authenticationOptions.Value);
        if (applyStarterCap && !serviceSession &&
            (_aiServicesClient == null || string.IsNullOrWhiteSpace(Options.Value.AiServiceUrl)))
        {
            // Stop before the install: otherwise the starter would be installed without its cap.
            throw new ToolException(
                $"Installing {StarterDefaultCap.StarterBlueprintName} into a child of the system tenant sets its default " +
                "LLM cap on the AI service, but the context has no AI service URL. Add it to the context, or install " +
                "the blueprint from a context of the child tenant and set the cap with SetAiSpendCap.");
        }

        var targetTenantId = Options.Value.TenantId;
        var client = ServiceClient;
        if (childTenantId != null)
        {
            (targetTenantId, client) = await (_childTenantAssetClientFactory ??
                                              throw new InvalidOperationException(
                                                  "No factory for --child-tenant-id is registered."))
                .CreateAsync(childTenantId);
        }

        Logger.LogInformation(
            "Installing blueprint '{BlueprintId}' into tenant '{TenantId}' at '{ServiceClientServiceUri}' (force={Force})",
            blueprintId, targetTenantId, client.ServiceUri, force);

        BlueprintApplyResultDto result;
        try
        {
            result = await client.ApplyBlueprintAsync(targetTenantId,
                new BlueprintApplyRequestDto
                {
                    BlueprintId = blueprintId, Force = force, ForceBreakingUpdate = forceBreakingUpdate
                });
        }
        catch (CkUpgradeBlockedException blocked)
        {
            throw BlueprintUpgradeGuardReport.Refused(Logger, "installation", blueprintId, blocked,
                BlueprintUpgradeGuardReport.ForceOption);
        }

        if (!result.Success)
        {
            Logger.LogError("Blueprint installation failed");
            return;
        }

        BlueprintUpgradeGuardReport.LogForced(Logger, BlueprintUpgradeGuardReport.ForceOption, result.Blockers);

        Logger.LogInformation(
            "Blueprint '{BlueprintId}' applied to tenant '{TenantId}' (mode={Mode}, seedFiles={SeedFiles})",
            result.BlueprintId, result.TenantId, result.ApplicationMode, result.SeedDataFilesApplied);

        BlueprintArchiveActivationReport.Log(Logger, result.ArchiveActivations, result.Warnings);

        var resultString = JsonConvert.SerializeObject(result, Formatting.Indented);
        _consoleService.WriteLine(resultString);

        if (applyStarterCap && serviceSession)
        {
            StarterDefaultCap.WarnSkipped(Logger,
                $"Blueprint '{blueprintId}' was installed into tenant '{targetTenantId}'", targetTenantId);
        }
        else if (applyStarterCap)
        {
            await StarterDefaultCap.ApplyAsync(Logger, _aiServicesClient!, _authenticationService, targetTenantId,
                $"Blueprint '{blueprintId}' was installed into tenant '{targetTenantId}'");
        }
    }
}
