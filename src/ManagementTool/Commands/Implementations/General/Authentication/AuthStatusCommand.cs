using System.IdentityModel.Tokens.Jwt;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Meshmakers.Common.CommandLineParser;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.General.Authentication;

internal class AuthStatusCommand : Command<OctoToolOptions>
{
    private readonly IOptions<OctoToolAuthenticationOptions> _authenticationOptions;
    private readonly IAuthenticationService _authenticationService;
    private readonly IAuthenticatorClient _authenticatorClient;
    private readonly IConsoleService _consoleService;
    private readonly IContextManager _contextManager;

    public AuthStatusCommand(ILogger<AuthStatusCommand> logger, IConsoleService consoleService,
        IOptions<OctoToolOptions> options,
        IOptions<OctoToolAuthenticationOptions> authenticationOptions, IAuthenticatorClient authenticatorClient,
        IAuthenticationService authenticationService, IContextManager contextManager)
        : base(logger, "AuthStatus", "Gets authentication status to the configured identity services.", options)
    {
        _consoleService = consoleService;
        _authenticationOptions = authenticationOptions;
        _authenticatorClient = authenticatorClient;
        _authenticationService = authenticationService;
        _contextManager = contextManager;
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Notes:
            [
                "- No parameters required. Reports the JWT claims of the current access token, including an `Auth Method` line indicating whether the token came from the device-code flow or `client_credentials`. For `client_credentials` tokens (no `sub` claim), the User Info section is omitted because the token is not user-bound.",
            ]
        );

    public override async Task Execute()
    {
        // Which context the reported token belongs to is the first thing to know here — more so
        // once --context can point the command at a context that is not the active one.
        Logger.LogInformation("Authentication status of context '{ContextName}'{OverrideHint}, file '{ContextFile}'",
            _contextManager.GetEffectiveContextName() ?? "<none>",
            _contextManager.IsContextOverridden ? $" (via --{Constants.ContextArgumentTerm})" : string.Empty,
            _contextManager.ConfigurationFilePath);

        Logger.LogInformation("Check of authentication status at \'{ValueIdentityServiceUrl}\' in progress...",
            Options.Value.IdentityServiceUrl);

        var result = await TestAuthenticationStatus();
        if (!result)
        {
            if (_authenticationOptions.Value.RefreshToken != null)
            {
                Logger.LogInformation("Refreshing token");

                try
                {
                    var authenticationData =
                        await _authenticatorClient.RefreshTokenAsync(_authenticationOptions.Value.RefreshToken);

                    _authenticationService.SaveAuthenticationData(authenticationData);

                    Logger.LogInformation("Refresh successful. Token expires at \'{AuthenticationDataExpiresAt}\'",
                        authenticationData.ExpiresAt);

                    await TestAuthenticationStatus();
                }
                catch (AuthenticationFailedException ex)
                {
                    // Refresh token gone/expired/revoked — the common outcome for a context that has
                    // not been used in a while (AB#4754). Report it actionably instead of letting the
                    // raw OIDC error bubble up.
                    Logger.LogWarning(
                        "The refresh token for context '{ContextName}' is no longer valid ({Reason}). " +
                        "Re-authenticate with: {LoginHint}",
                        _contextManager.GetEffectiveContextName() ?? "<none>", ex.Message, BuildLoginHint());
                }
            }
            else
            {
                // client_credentials session (no refresh token) with an expired access token.
                Logger.LogInformation(
                    "No refresh token available (client_credentials session). Re-run 'LogInClientCredentials' " +
                    "or set {EnvClientId} / {EnvClientSecret} so the token is re-acquired automatically.",
                    Constants.EnvVarClientId, Constants.EnvVarClientSecret);
            }
        }
    }

    private string BuildLoginHint()
    {
        var contextName = _contextManager.GetEffectiveContextName();
        return contextName != null
            ? $"octo-cli --{Constants.ContextArgumentTerm} {contextName} -c LogIn"
            : "octo-cli -c LogIn";
    }

    private async Task<bool> TestAuthenticationStatus()
    {
        var accessToken = _authenticationOptions.Value.AccessToken;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            Logger.LogInformation("Access token is not set");
            return false;
        }

        JwtSecurityToken token;
        try
        {
            token = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        }
        catch (Exception ex)
        {
            Logger.LogInformation("Access token is INVALID (not a parseable JWT): {Reason}", ex.Message);
            return false;
        }

        // ValidTo is UTC by JwtSecurityToken convention; compare against UtcNow.
        if (token.ValidTo != default && token.ValidTo < DateTime.UtcNow)
        {
            Logger.LogInformation("Access token is EXPIRED (valid to '{ValidTo}')",
                token.ValidTo.ToString("o"));
            return false;
        }

        // Detect token type from claims, not from refresh-token presence:
        //   - user token (device code, password, etc.) → has 'sub' claim
        //   - client_credentials → no 'sub' claim, has 'client_id' claim only
        var isClientCredentials = string.IsNullOrEmpty(token.Subject);

        Meshmakers.Octo.Sdk.ServiceClient.Authorization.UserInfoData? userInfoData = null;
        if (!isClientCredentials)
        {
            // User-bound token — call /connect/userinfo for richer claims.
            userInfoData = await _authenticatorClient.GetUserInfoAsync(accessToken);
            if (!userInfoData.IsAuthenticated)
            {
                Logger.LogInformation(
                    "Access token has 'sub' claim but /connect/userinfo rejected it; treating as INVALID");
                return false;
            }
        }

        Logger.LogInformation(isClientCredentials
            ? "Access token is valid (client_credentials — userinfo not applicable)"
            : "Access token is valid");

        // Display token information
        _consoleService.WriteLine("");
        _consoleService.WriteLine("==========================================");
        _consoleService.WriteLine("Access Token Information");
        _consoleService.WriteLine("==========================================");
        _consoleService.WriteColumnLine("KEY", 20, "VALUE");
        _consoleService.WriteColumnLine("Issuer", 20, token.Issuer);
        _consoleService.WriteColumnLine("Audience", 20, string.Join(", ", token.Audiences));
        _consoleService.WriteColumnLine("Issued At", 20, token.IssuedAt.ToString("o"));
        _consoleService.WriteColumnLine("Valid From", 20, token.ValidFrom.ToString("o"));
        _consoleService.WriteColumnLine("Valid To", 20, token.ValidTo.ToString("o"));
        _consoleService.WriteColumnLine("Subject", 20,
            isClientCredentials ? "(none — client_credentials)" : token.Subject);
        _consoleService.WriteColumnLine("Token Type", 20, token.Header["typ"]?.ToString() ?? "N/A");
        _consoleService.WriteColumnLine("Algorithm", 20, token.Header["alg"]?.ToString() ?? "N/A");
        _consoleService.WriteColumnLine("Signature", 20, token.RawSignature);

        var authMethod = isClientCredentials
            ? $"client_credentials (no refresh token; re-login or set {Constants.EnvVarClientId} / {Constants.EnvVarClientSecret} for auto re-login)"
            : "device code (refresh token available)";
        _consoleService.WriteColumnLine("Auth Method", 20, authMethod);

        _consoleService.WriteLine("");
        _consoleService.WriteLine("CLAIMS");
        foreach (var tokenClaim in token.Claims)
        {
            _consoleService.WriteColumnLine(tokenClaim.Type, 20, tokenClaim.Value);
        }

        if (userInfoData != null)
        {
            _consoleService.WriteLine("");
            _consoleService.WriteLine("==========================================");
            _consoleService.WriteLine("User Info Endpoint Response");
            _consoleService.WriteLine("==========================================");
            _consoleService.WriteColumnLine("KEY", 20, "VALUE");

            if (userInfoData.Claims != null)
            {
                foreach (var claim in userInfoData.Claims)
                {
                    _consoleService.WriteColumnLine(claim.Type, 20, claim.Value);
                }
            }
        }

        _consoleService.WriteLine("");
        _consoleService.WriteLine("ACCESS TOKEN");
        _consoleService.WriteLine(accessToken);

        return true;
    }
}