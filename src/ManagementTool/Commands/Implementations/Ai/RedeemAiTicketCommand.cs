using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AiServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Ai;

/// <summary>
///     Bastion-side ticket-redeem command (concept §5a, work item #4123). The operator runs
///     this on a separate machine — typically the host that has just completed an Anthropic
///     subscription login via <c>claude /login</c> — to hand the freshly minted token pair
///     to the AI Adapter. The ticket code is the auth artefact, so this command runs
///     anonymously: no OctoMesh user session is needed, and <see cref="PreValidate" />
///     deliberately skips the bearer flow the rest of the CLI uses.
///
///     For developer-machine smoke testing the four token-related args (<c>-at -rt -aex
///     -rex</c>) are optional. When omitted the CLI substitutes a fake-token pair with a
///     far-future expiry — useful because the locally-spawned claude CLI ignores the
///     workspace <c>.credentials.json</c> in favour of the user's host keychain, so the
///     persisted tokens never reach an Anthropic endpoint. A loud warning prints whenever
///     defaults are used so a bastion operator can't ship a fake by accident.
/// </summary>
internal class RedeemAiTicketCommand : ServiceClientOctoCommand<IAiServicesClient>
{
    private const string DevFakeAccessToken = "sk-ant-dev-fake-access";
    private const string DevFakeRefreshToken = "sk-ant-dev-fake-refresh";
    private const string DevFakeAccessExpiresAt = "2099-01-01T00:00:00Z";
    private const string DevFakeRefreshExpiresAt = "2099-12-31T00:00:00Z";

    private readonly IArgument _tenantArg;
    private readonly IArgument _codeArg;
    private readonly IArgument _accessTokenArg;
    private readonly IArgument _refreshTokenArg;
    private readonly IArgument _accessExpiresAtArg;
    private readonly IArgument _refreshExpiresAtArg;

    public RedeemAiTicketCommand(ILogger<RedeemAiTicketCommand> logger, IOptions<OctoToolOptions> options,
        IAiServicesClient aiServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.AiServicesGroup, "RedeemAiTicket",
            "Redeems a one-time AI credential ticket and persists the Anthropic subscription tokens on the AI Adapter. " +
            "Runs anonymously — the ticket code authenticates the call, no OctoMesh login required. " +
            "Token-related args (-at -rt -aex -rex) are optional for developer smoke tests; missing values default to a fake pair with a far-future expiry.",
            options, aiServicesClient, authenticationService)
    {
        _tenantArg = CommandArgumentValue.AddArgument("tid", "tenantId",
            ["Tenant the ticket was issued for"], true, 1);
        _codeArg = CommandArgumentValue.AddArgument("tc", "ticketCode",
            ["One-time code from the admin's Refinery Studio modal"], true, 1);
        _accessTokenArg = CommandArgumentValue.AddArgument("at", "accessToken",
            ["Plaintext Anthropic access token. Optional — defaults to a fake for dev."], false, 1);
        _refreshTokenArg = CommandArgumentValue.AddArgument("rt", "refreshToken",
            ["Plaintext Anthropic refresh token. Optional — defaults to a fake for dev."], false, 1);
        _accessExpiresAtArg = CommandArgumentValue.AddArgument("aex", "accessExpiresAt",
            ["UTC expiry of the access token (ISO-8601). Optional — defaults to 2099 for dev."], false, 1);
        _refreshExpiresAtArg = CommandArgumentValue.AddArgument("rex", "refreshExpiresAt",
            ["UTC expiry of the refresh token (ISO-8601). Optional — defaults to 2099 for dev."], false, 1);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Bypass the bearer-auth check the base class runs. The redeem endpoint is
    ///     <c>[AllowAnonymous]</c> — sending a stale or wrong token here would only
    ///     confuse the operator on the bastion (who almost certainly has no OctoMesh
    ///     session at all).
    /// </remarks>
    public override Task PreValidate()
    {
        Logger.LogInformation("Service URI: {ServiceClientServiceUri}", ServiceClient.ServiceUri);
        return Task.CompletedTask;
    }

    public override async Task Execute()
    {
        var tenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantArg);
        var code = CommandArgumentValue.GetArgumentScalarValue<string>(_codeArg);

        var (accessToken, refreshToken, accessExpiresAtRaw, refreshExpiresAtRaw, usedDevDefaults)
            = ResolveTokenArgsOrDefaults();

        // Parse expiry timestamps with AssumeUniversal so a value without a 'Z' suffix
        // doesn't accidentally land in the server's clock as local time — the server
        // stores everything UTC, and the bastion operator shouldn't have to think about
        // their machine's TZ.
        var accessExpiresAt = DateTime.Parse(accessExpiresAtRaw,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
        var refreshExpiresAt = DateTime.Parse(refreshExpiresAtRaw,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);

        if (usedDevDefaults)
        {
            // Loud — accidental fake-PAT in a production redeem would surface as a
            // mystery 401 from Anthropic the first time the worker tried to call the
            // API. Better to scream now.
            Logger.LogWarning(
                "DEV MODE: redeeming ticket '{TicketCode}' with FAKE Anthropic tokens. Pass -at / -rt / -aex / -rex for real tokens.",
                code);
        }

        // Log code + expiries only — never the token plaintext. A tail of the log
        // file should not be enough to walk away with a working subscription.
        Logger.LogInformation(
            "Redeeming AI credential ticket '{TicketCode}' for tenant '{TenantId}' at '{ServiceClientServiceUri}' (access exp {AccessExpiresAt}, refresh exp {RefreshExpiresAt})",
            code, tenantId, ServiceClient.ServiceUri, accessExpiresAt, refreshExpiresAt);

        var status = await ServiceClient.RedeemTicketAsync(
            tenantId, code, accessToken, refreshToken, accessExpiresAt, refreshExpiresAt);

        Logger.LogInformation(
            "Ticket redeemed: lease status={Status}, generation={Generation}, access exp={AccessExpiresAt}, refresh exp={RefreshExpiresAt}",
            status.Status, status.Generation, status.AccessExpiresAt, status.RefreshExpiresAt);
    }

    private (string AccessToken, string RefreshToken, string AccessExpiresAt, string RefreshExpiresAt, bool UsedDevDefaults)
        ResolveTokenArgsOrDefaults()
    {
        var hasAccess = CommandArgumentValue.IsArgumentUsed(_accessTokenArg);
        var hasRefresh = CommandArgumentValue.IsArgumentUsed(_refreshTokenArg);
        var hasAccessExp = CommandArgumentValue.IsArgumentUsed(_accessExpiresAtArg);
        var hasRefreshExp = CommandArgumentValue.IsArgumentUsed(_refreshExpiresAtArg);

        // Treat as dev mode only when ALL four are absent. Partial supply is a likely
        // operator mistake (e.g. they pasted the access token but forgot the refresh) —
        // failing loudly there is friendlier than silently substituting two fakes.
        if (!hasAccess && !hasRefresh && !hasAccessExp && !hasRefreshExp)
        {
            return (DevFakeAccessToken, DevFakeRefreshToken,
                DevFakeAccessExpiresAt, DevFakeRefreshExpiresAt, true);
        }

        if (!hasAccess || !hasRefresh || !hasAccessExp || !hasRefreshExp)
        {
            throw new ToolException(
                "Partial token args. Supply all of -at -rt -aex -rex together, or none to fall back to the dev fakes.");
        }

        return (
            CommandArgumentValue.GetArgumentScalarValue<string>(_accessTokenArg),
            CommandArgumentValue.GetArgumentScalarValue<string>(_refreshTokenArg),
            CommandArgumentValue.GetArgumentScalarValue<string>(_accessExpiresAtArg),
            CommandArgumentValue.GetArgumentScalarValue<string>(_refreshExpiresAtArg),
            false);
    }
}
