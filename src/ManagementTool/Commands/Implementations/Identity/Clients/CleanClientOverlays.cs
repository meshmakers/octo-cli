using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;

internal class CleanClientOverlays : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IdentityMethodCall _methods;
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _overlayName;
    private readonly IArgument _yesArg;

    public CleanClientOverlays(ILogger<CleanClientOverlays> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.IdentityServicesGroup, "CleanClientOverlays",
            "Strips overlay URI entries from every blueprint-managed client in the tenant. " +
            "Without -overlayName: removes every Source matching 'overlay:*'. " +
            "With -overlayName: removes only entries matching 'overlay:<name>' exactly. " +
            "Destructive — typical use is before a sanitised DumpTenant export; the operator " +
            "can re-apply with Apply-IdentityOverlay afterwards (idempotent, no-op if state matches).",
            options, identityServicesClient, authenticationService)
    {
        _methods = new IdentityMethodCall(tenantClient, authenticationService, logger);
        _confirmationService = confirmationService;

        _overlayName = CommandArgumentValue.AddArgument("n", "overlayName",
            ["Optional overlay name. Without it, every 'overlay:*' source is removed. With it, " +
             "only 'overlay:<name>' is removed. Same regex constraint as ApplyClientOverlay " +
             "([A-Za-z0-9._-]+)."],
            false, 1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(
                    arguments: [],
                    description: "Strip every overlay:* entry from the active tenant (prompts for confirmation)"),
                new CodeSample(
                    arguments: [new CodeSampleArgument(_overlayName, "local-dev")],
                    description: "Strip only overlay:local-dev entries; gerald-laptop and other overlays survive"),
                new CodeSample(
                    arguments: [new CodeSampleArgument(_yesArg)],
                    description: "Skip the confirmation prompt — typical CI / dump-pipeline usage"),
            ],
            Notes:
            [
                "Idempotent — clients with nothing to remove skip the per-client UpdateAsync + cache invalidation.",
                "base / api / family:* entries are always preserved; only overlay:* matches are removed.",
                "Typical workflow before sharing a tenant dump: CleanClientOverlays -y → DumpTenant → Apply-IdentityOverlay (re-applies the canonical local-dev set from octo-tools/overlays/identity-local-dev.yaml).",
            ]);

    public override async Task Execute()
    {
        var overlayName = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_overlayName);

        var promptSubject = string.IsNullOrEmpty(overlayName)
            ? "ALL overlay:* URI entries on every client"
            : $"every overlay:{overlayName} URI entry on every client";

        if (!CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm($"Are you sure you want to strip {promptSubject}?"))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation(
            "Stripping overlay entries (filter: {Filter}) at '{ServiceClientServiceUri}'",
            string.IsNullOrEmpty(overlayName) ? "every overlay:*" : $"overlay:{overlayName}",
            ServiceClient.ServiceUri);

        var value = await _methods.RunAsync(RtClientMethodClient.CleanOverlayEntriesMethodId,
            () => new RtClientMethodClient(_methods.TenantClient).CleanOverlayEntriesAsync(
                string.IsNullOrEmpty(overlayName) ? null : overlayName));
        LogClean(value?.TotalEntriesRemoved ?? 0, value?.ClientsAffected ?? 0,
            (value?.ClientResults ?? []).Select(c => (c.ClientId, c.RedirectUrisRemoved ?? 0,
                c.PostLogoutRedirectUrisRemoved ?? 0, c.AllowedCorsOriginsRemoved ?? 0)));
    }

    private void LogClean(long total, long clientsAffected,
        IEnumerable<(string? ClientId, long Redirect, long PostLogout, long Cors)> clientResults)
    {
        Logger.LogInformation(
            "Clean complete: {Total} entries removed across {Clients} client(s)",
            total, clientsAffected);

        foreach (var clientResult in clientResults)
        {
            Logger.LogInformation(
                "  [{ClientId}] RedirectUris={R}, PostLogoutRedirectUris={PL}, AllowedCorsOrigins={CO}",
                clientResult.ClientId,
                clientResult.Redirect,
                clientResult.PostLogout,
                clientResult.Cors);
        }
    }
}
