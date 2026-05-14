using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Blueprints;

internal class UninstallBlueprint : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _blueprintNameArg;
    private readonly IArgument _cascadeArg;
    private readonly IArgument _yesArg;

    public UninstallBlueprint(
        ILogger<UninstallBlueprint> logger,
        IConsoleService consoleService,
        IConfirmationService confirmationService,
        IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "UninstallBlueprint",
            "Removes a blueprint from the active tenant; with --cascade, dependents and orphan dependencies go too.",
            options, assetServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _confirmationService = confirmationService;

        _blueprintNameArg = CommandArgumentValue.AddArgument("n", "blueprintName",
            ["Blueprint name (without version), e.g. 'MyBlueprint'"], true, 1);

        _cascadeArg = CommandArgumentValue.AddArgument("c", "cascade",
            ["Also uninstall blueprints that depend on the target, and orphan dependencies of the target"],
            false, 0);

        _yesArg = CommandArgumentValue.AddArgument("y", "yes",
            ["Skip the interactive confirmation prompt"], false, 0);
    }

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing - configure it via the active context");
            return;
        }

        var blueprintName = CommandArgumentValue.GetArgumentScalarValue<string>(_blueprintNameArg);
        var cascade = CommandArgumentValue.IsArgumentUsed(_cascadeArg);
        var skipConfirmation = CommandArgumentValue.IsArgumentUsed(_yesArg);

        var promptDetail = cascade
            ? $" together with any blueprints that depend on it and any orphaned dependencies"
            : string.Empty;

        if (!skipConfirmation && !_confirmationService.Confirm(
                $"uninstall blueprint '{blueprintName}' from tenant '{Options.Value.TenantId}'{promptDetail}? Locked owned entities will be erased"))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation(
            "Uninstalling blueprint '{BlueprintName}' from tenant '{TenantId}' (cascade={Cascade})",
            blueprintName, Options.Value.TenantId, cascade);

        var result = await ServiceClient.UninstallBlueprintAsync(
            Options.Value.TenantId, blueprintName, cascade);

        if (!result.Success)
        {
            if (result.BlockingDependents.Count > 0)
            {
                Logger.LogError(
                    "Uninstall blocked: '{BlueprintName}' is still required by {Dependents}. Re-run with --cascade to remove them as well.",
                    blueprintName, string.Join(", ", result.BlockingDependents));
            }
            else
            {
                Logger.LogError("Uninstall failed");
            }

            _consoleService.WriteLine(JsonConvert.SerializeObject(result, Formatting.Indented));
            return;
        }

        Logger.LogInformation(
            "Uninstall complete: {EntitiesDeleted} entities erased, {CascadedCount} cascaded blueprints",
            result.EntitiesDeleted, result.CascadedDependencies.Count);

        _consoleService.WriteLine(JsonConvert.SerializeObject(result, Formatting.Indented));
    }
}
