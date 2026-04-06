using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.CkModelLibraries;

internal class LibraryStatusCommand : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IArgument _needsActionArg;
    private readonly IArgument _installedOnlyArg;

    public LibraryStatusCommand(ILogger<LibraryStatusCommand> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "LibraryStatus",
            "Shows installed CK model libraries with catalog availability. Use --needs-action to filter.",
            options, assetServicesClient, authenticationService)
    {
        _needsActionArg = CommandArgumentValue.AddArgument("na", "needs-action",
            ["Show only models that need action"], false, 0);
        _installedOnlyArg = CommandArgumentValue.AddArgument("io", "installed-only",
            ["Show only installed models"], false, 0);
    }

    public override async Task Execute()
    {
        var tenantId = Options.Value.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
            throw ToolException.NoTenantIdConfigured();

        var status = await ServiceClient.GetLibraryStatusAsync(tenantId);
        var items = status.Items;

        if (CommandArgumentValue.IsArgumentUsed(_needsActionArg))
            items = items.Where(i => i.NeedsAction).ToList();
        if (CommandArgumentValue.IsArgumentUsed(_installedOnlyArg))
            items = items.Where(i => i.InstalledVersion != null).ToList();

        Logger.LogInformation("{Count} model(s) ({NeedsAction} need action):", items.Count, status.ModelsNeedingActionCount);
        Logger.LogInformation("{N,-25} {I,-12} {C,-12} {S,-15} {A}", "NAME", "INSTALLED", "CATALOG", "STATE", "ACTION");
        Logger.LogInformation("{S1,-25} {S2,-12} {S3,-12} {S4,-15} {S5}", new string('-', 25), new string('-', 12), new string('-', 12), new string('-', 15), new string('-', 20));

        foreach (var item in items)
        {
            var action = item.IsServiceManaged ? "Service-Managed"
                : !item.IsCompatible ? "Incompatible"
                : item.NeedsAction ? (item.HasUpdate ? "Update" : "Fix")
                : item.InstalledVersion == null ? "Install"
                : "-";
            Logger.LogInformation("{N,-25} {I,-12} {C,-12} {S,-15} {A}",
                item.Name, item.InstalledVersion ?? "-", item.CatalogVersion ?? "-",
                item.ModelState ?? "Not Installed", action);
        }
    }
}
