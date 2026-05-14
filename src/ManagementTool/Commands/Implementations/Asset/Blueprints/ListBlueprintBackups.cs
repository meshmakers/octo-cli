using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Blueprints;

internal class ListBlueprintBackups : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IConsoleService _consoleService;

    public ListBlueprintBackups(
        ILogger<ListBlueprintBackups> logger,
        IConsoleService consoleService,
        IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "ListBlueprintBackups",
            "Lists tenant backups created before blueprint updates.",
            options, assetServicesClient, authenticationService)
    {
        _consoleService = consoleService;
    }

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing - configure it via the active context");
            return;
        }

        Logger.LogInformation(
            "Listing blueprint backups for tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            Options.Value.TenantId, ServiceClient.ServiceUri);

        var backups = await ServiceClient.ListBlueprintBackupsAsync(Options.Value.TenantId);

        if (backups.Count == 0)
        {
            Logger.LogInformation("No backups found for tenant '{TenantId}'", Options.Value.TenantId);
            return;
        }

        var resultString = JsonConvert.SerializeObject(backups, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }
}
