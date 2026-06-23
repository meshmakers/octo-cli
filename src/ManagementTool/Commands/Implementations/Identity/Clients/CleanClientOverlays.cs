using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;

internal class CleanClientOverlays : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _overlayName;
    private readonly IArgument _yesArg;

    public CleanClientOverlays(ILogger<CleanClientOverlays> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.IdentityServicesGroup, "CleanClientOverlays",
            "Strips overlay URI entries from every blueprint-managed client in the tenant. " +
            "Without -overlayName: removes every Source matching 'overlay:*'. " +
            "With -overlayName: removes only entries matching 'overlay:<name>' exactly. " +
            "Destructive — typical use is before a sanitised DumpTenant export; the operator " +
            "can re-apply with Apply-IdentityOverlay afterwards (idempotent, no-op if state matches).",
            options, identityServicesClient, authenticationService)
    {
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

        var result = await ServiceClient.CleanOverlayEntries(overlayName);

        Logger.LogInformation(
            "Clean complete: {Total} entries removed across {Clients} client(s)",
            result.TotalEntriesRemoved, result.ClientsAffected);

        foreach (var clientResult in result.ClientResults)
        {
            Logger.LogInformation(
                "  [{ClientId}] RedirectUris={R}, PostLogoutRedirectUris={PL}, AllowedCorsOrigins={CO}",
                clientResult.ClientId,
                clientResult.RedirectUrisRemoved,
                clientResult.PostLogoutRedirectUrisRemoved,
                clientResult.AllowedCorsOriginsRemoved);
        }
    }
}
