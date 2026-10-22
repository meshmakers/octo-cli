using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.TenantLifecycle;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;

internal class DeleteTenant : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IConfirmationService _confirmationService;
    private readonly IConsoleService _consoleService;
    private readonly IChildTenantAccess _childTenantAccess;
    private readonly IChildTenantFeaturesFactory _childTenantFeaturesFactory;
    private readonly IArgument _tenantIdArg;
    private readonly IArgument _yesArg;
    private readonly IArgument _disableFeaturesArg;

    public DeleteTenant(ILogger<DeleteTenant> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IAuthenticationService authenticationService,
        IConfirmationService confirmationService, IConsoleService consoleService,
        IChildTenantAccess childTenantAccess, IChildTenantFeaturesFactory childTenantFeaturesFactory)
        : base(logger, Constants.AssetRepositoryServicesGroup, "Delete", "Deletes an existing tenant.", options,
            assetServicesClient, authenticationService)
    {
        _confirmationService = confirmationService;
        _consoleService = consoleService;
        _childTenantAccess = childTenantAccess;
        _childTenantFeaturesFactory = childTenantFeaturesFactory;

        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId", ["Id of tenant"],
            true, 1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
        _disableFeaturesArg = CommandArgumentValue.AddArgument("df", "disable-features",
            ["Switch off every capability the tenant has enabled (Stream Data, Communication, Reporting, AI Services) " +
             "with your token for that tenant, then delete it. Requires -y."], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_tenantIdArg, "newtenant")], description: "Basic usage"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_tenantIdArg, "newtenant"),
                    new CodeSampleArgument(_yesArg),
                ],
                    description: "Non-interactive"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_tenantIdArg, "scratch1"),
                    new CodeSampleArgument(_disableFeaturesArg),
                    new CodeSampleArgument(_yesArg),
                ],
                    description: "Clean up a scratch tenant: switch its capabilities off, then delete it"),
            ],
            Notes:
            [
                "Refused with HTTP 409 while Stream Data, Communication, Reporting or AI Services is still enabled for the tenant; " +
                "the error names the enabled capabilities. Disable them first with DisableStreamData / DisableCommunication / " +
                "DisableReporting / DisableAi. Those commands act on the tenant of the active context, so switch to the tenant " +
                "being deleted with UseContext or pass --context <name> - or let --disable-features do it.",
                "--disable-features (AB#7192) switches off every enabled capability for you and deletes the tenant afterwards. " +
                "The capability endpoints belong to the tenant's own administrator, so the command uses YOUR token for the tenant " +
                "being deleted: with a client-credentials session (OCTO_CLI_CLIENT_ID / OCTO_CLI_CLIENT_SECRET set) the client's " +
                "mirror in that tenant - the creator of a scratch tenant (Create --scratch) is its administrator; with a user " +
                "session the user's token is exchanged for the tenant and the user must be its administrator. It requires -y, " +
                "prints the capabilities it switched off, and never deletes archives: when Stream Data stays enabled because " +
                "archives are still activated, the command reports them, does NOT delete the tenant and exits with an error " +
                "(DisableArchive keeps the data, DeleteArchive removes the archive).",
                "If the tenant's data is still needed, take a backup with Dump before disabling the capabilities and deleting; " +
                "Dump works regardless of capability state.",
                "Answers 404 when the tenant is not a child of the current tenant.",
                "A 409 is also answered while the tenant is still being created; that message names the lifecycle state, " +
                "not capabilities - retry once the tenant is active or failed.",
            ]
        );

    public override async Task Execute()
    {
        var tenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg).ToLower();
        var disableFeatures = CommandArgumentValue.IsArgumentUsed(_disableFeaturesArg);
        var confirmed = CommandArgumentValue.IsArgumentUsed(_yesArg);

        if (disableFeatures && !confirmed)
        {
            throw new ToolException(
                $"--disable-features switches off the capabilities of tenant '{tenantId}' and deletes the tenant without " +
                "a further prompt: repeat the command with -y to confirm.");
        }

        if (!confirmed &&
            !_confirmationService.Confirm(
                $"Are you sure you want to delete tenant '{tenantId}' of {ParentScopeDescription}?"))
        {
            throw ToolException.OperationCancelledByUser();
        }

        if (disableFeatures)
        {
            await DisableFeaturesAsync(tenantId);
        }

        Logger.LogInformation("Deleting tenant \'{TenantId}\' on at \'{ServiceClientServiceUri}\'", tenantId,
            ServiceClient.ServiceUri);

        await ServiceClient.DeleteTenantAsync(tenantId);

        Logger.LogInformation("Tenant \'{TenantId}\' on at \'{ServiceClientServiceUri}\' deleted", tenantId,
            ServiceClient.ServiceUri);
    }

    private async Task DisableFeaturesAsync(string tenantId)
    {
        var token = await _childTenantAccess.GetAccessTokenAsync(tenantId);
        var features = _childTenantFeaturesFactory.Create(tenantId, token);

        var status = await features.GetStatusAsync();
        var enabled = TenantFeatureDisabler.GetEnabled(status);
        if (enabled.Count == 0)
        {
            _consoleService.WriteLine($"Tenant '{tenantId}': no capability is enabled.");
            return;
        }

        var report = await TenantFeatureDisabler.DisableEnabledAsync(status, features.Switches);

        foreach (var result in report.Results)
        {
            var name = TenantFeatureDisabler.DisplayName(result.Feature);
            _consoleService.WriteLine(result.Disabled
                ? $"Disabled {name} of tenant '{tenantId}'."
                : $"{name} of tenant '{tenantId}' stays enabled: {result.Error}");
        }

        if (!report.AllDisabled)
        {
            var stays = string.Join(", ", report.Failed.Select(f => TenantFeatureDisabler.DisplayName(f.Feature)));
            throw new ToolException(
                $"Tenant '{tenantId}' was not deleted: {stays} could not be disabled (see above). Activated archives are " +
                "reported, never deleted by this command - disable them with DisableArchive (data is kept) or remove them " +
                "with DeleteArchive (rollups before their source archive), then repeat the command.");
        }
    }
}
