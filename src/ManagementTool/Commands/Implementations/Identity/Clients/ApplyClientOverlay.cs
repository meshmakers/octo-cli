using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;

internal class ApplyClientOverlay : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _clientId;
    private readonly IArgument _overlayName;
    private readonly IArgument _redirectUris;
    private readonly IArgument _postLogoutRedirectUris;
    private readonly IArgument _allowedCorsOrigins;

    public ApplyClientOverlay(ILogger<ApplyClientOverlay> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "ApplyClientOverlay",
            "Applies an overlay URI set (RedirectUris / PostLogoutRedirectUris / AllowedCorsOrigins) " +
            "to a blueprint-managed client. New entries are written with Source = 'overlay:<OverlayName>', " +
            "kept across blueprint re-apply by the Step 2a preservation pass. Idempotent — duplicates are " +
            "silently skipped (any source). At least one of the three URI lists must be provided.",
            options, identityServicesClient, authenticationService)
    {
        _clientId = CommandArgumentValue.AddArgument("id", "clientId",
            ["The ClientId to apply the overlay to. Must already exist."], true, 1);
        _overlayName = CommandArgumentValue.AddArgument("n", "overlayName",
            ["Operator-meaningful overlay name. Becomes the suffix of 'overlay:<OverlayName>' on every " +
             "persisted entry. Constrained to [A-Za-z0-9._-]+."],
            true, 1);
        _redirectUris = CommandArgumentValue.AddArgument("r", "redirectUris",
            ["Comma-separated list of redirect URIs to add"], false, 1);
        _postLogoutRedirectUris = CommandArgumentValue.AddArgument("plr", "postLogoutRedirectUris",
            ["Comma-separated list of post-logout redirect URIs to add"], false, 1);
        _allowedCorsOrigins = CommandArgumentValue.AddArgument("co", "allowedCorsOrigins",
            ["Comma-separated list of CORS origins to add (pass without trailing slash)"], false, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(
                    arguments:
                    [
                        new CodeSampleArgument(_clientId, "octo-data-refinery-studio"),
                        new CodeSampleArgument(_overlayName, "local-dev"),
                        new CodeSampleArgument(_redirectUris, "http://localhost:4200/auth-callback,http://localhost:4200/silent-callback"),
                        new CodeSampleArgument(_postLogoutRedirectUris, "http://localhost:4200/"),
                        new CodeSampleArgument(_allowedCorsOrigins, "http://localhost:4200"),
                    ],
                    description: "Inject the local-dev redirect / post-logout / CORS triple into the Refinery Studio client"),
                new CodeSample(
                    arguments:
                    [
                        new CodeSampleArgument(_clientId, "octo-data-refinery-studio"),
                        new CodeSampleArgument(_overlayName, "local-dev"),
                        new CodeSampleArgument(_redirectUris, "http://localhost:4200/auth-callback"),
                    ],
                    description: "Single-list usage — only RedirectUris is added; other lists are untouched"),
            ],
            Notes:
            [
                "Re-running with the same payload is a no-op: every URI hits the dedup branch, no DB write, no cache invalidation.",
                "Server returns 400 if every supplied list is empty / whitespace-only.",
                "The endpoint does not strip trailing slashes from CORS origins — pass origin-shaped values yourself.",
            ]);

    public override async Task Execute()
    {
        var clientId = CommandArgumentValue.GetArgumentScalarValue<string>(_clientId);
        var overlayName = CommandArgumentValue.GetArgumentScalarValue<string>(_overlayName);

        var redirectUris = ParseCommaSeparatedList(_redirectUris);
        var postLogoutRedirectUris = ParseCommaSeparatedList(_postLogoutRedirectUris);
        var allowedCorsOrigins = ParseCommaSeparatedList(_allowedCorsOrigins);

        // Client-side guard: matches the server's 400 on empty payloads. Catches the
        // common typo case (`-r " , "` trims to zero entries) before a roundtrip so the
        // operator gets a CLI-shaped error instead of a wrapped HTTP exception.
        if ((redirectUris == null || redirectUris.Count == 0) &&
            (postLogoutRedirectUris == null || postLogoutRedirectUris.Count == 0) &&
            (allowedCorsOrigins == null || allowedCorsOrigins.Count == 0))
        {
            throw new ToolException(
                "At least one of -r / -plr / -co must contain a non-whitespace URI.");
        }

        var dto = new ApplyOverlayUrisDto
        {
            OverlayName = overlayName,
            RedirectUris = redirectUris,
            PostLogoutRedirectUris = postLogoutRedirectUris,
            AllowedCorsOrigins = allowedCorsOrigins
        };

        Logger.LogInformation(
            "Applying overlay '{OverlayName}' to client '{ClientId}' at '{ServiceClientServiceUri}'",
            overlayName, clientId, ServiceClient.ServiceUri);

        var result = await ServiceClient.ApplyClientOverlay(clientId, dto);

        Logger.LogInformation(
            "Overlay '{OverlayName}' on '{ClientId}' applied: " +
            "RedirectUris={RAdd} added / {RSkip} skipped, " +
            "PostLogoutRedirectUris={PLAdd} added / {PLSkip} skipped, " +
            "AllowedCorsOrigins={COAdd} added / {COSkip} skipped",
            result.OverlayName, result.ClientId,
            result.RedirectUris.Added, result.RedirectUris.SkippedDuplicate,
            result.PostLogoutRedirectUris.Added, result.PostLogoutRedirectUris.SkippedDuplicate,
            result.AllowedCorsOrigins.Added, result.AllowedCorsOrigins.SkippedDuplicate);
    }

    private List<string>? ParseCommaSeparatedList(IArgument argument)
    {
        if (!CommandArgumentValue.IsArgumentUsed(argument))
        {
            return null;
        }

        var value = CommandArgumentValue.GetArgumentScalarValue<string>(argument);
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
}
