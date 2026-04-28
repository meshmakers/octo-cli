using System.IdentityModel.Tokens.Jwt;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.General.Authentication;

internal class AuthStatusCommand : Command<OctoToolOptions>
{
    private readonly IOptions<OctoToolAuthenticationOptions> _authenticationOptions;
    private readonly IAuthenticationService _authenticationService;
    private readonly IAuthenticatorClient _authenticatorClient;
    private readonly IConsoleService _consoleService;

    public AuthStatusCommand(ILogger<AuthStatusCommand> logger, IConsoleService consoleService,
        IOptions<OctoToolOptions> options,
        IOptions<OctoToolAuthenticationOptions> authenticationOptions, IAuthenticatorClient authenticatorClient,
        IAuthenticationService authenticationService)
        : base(logger, "AuthStatus", "Gets authentication status to the configured identity services.", options)
    {
        _consoleService = consoleService;
        _authenticationOptions = authenticationOptions;
        _authenticatorClient = authenticatorClient;
        _authenticationService = authenticationService;
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Check of authentication status at \'{ValueIdentityServiceUrl}\' in progress...",
            Options.Value.IdentityServiceUrl);

        var result = await TestAuthenticationStatus();
        if (!result)
        {
            Logger.LogInformation("Refreshing token");

            if (_authenticationOptions.Value.RefreshToken != null)
            {
                var authenticationData =
                    await _authenticatorClient.RefreshTokenAsync(_authenticationOptions.Value.RefreshToken);

                _authenticationService.SaveAuthenticationData(authenticationData);

                Logger.LogInformation("Refresh successful. Token expires at \'{AuthenticationDataExpiresAt}\'",
                    authenticationData.ExpiresAt);
            }

            await TestAuthenticationStatus();
        }
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