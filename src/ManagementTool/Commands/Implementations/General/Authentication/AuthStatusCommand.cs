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
        if (!string.IsNullOrWhiteSpace(_authenticationOptions.Value.AccessToken))
        {
            var userInfoData = await _authenticatorClient.GetUserInfoAsync(_authenticationOptions.Value.AccessToken);

            if (userInfoData.IsAuthenticated)
            {
                Logger.LogInformation("Access token is valid");

                var handler = new JwtSecurityTokenHandler();
                var token = handler.ReadJwtToken(_authenticationOptions.Value.AccessToken);

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
                _consoleService.WriteColumnLine("Subject", 20, token.Subject);
                _consoleService.WriteColumnLine("Token Type", 20, token.Header["typ"]?.ToString() ?? "N/A");
                _consoleService.WriteColumnLine("Algorithm", 20, token.Header["alg"]?.ToString() ?? "N/A");
                _consoleService.WriteColumnLine("Signature", 20, token.RawSignature);
                _consoleService.WriteLine("");
                _consoleService.WriteLine("CLAIMS");
                foreach (var tokenClaim in token.Claims)
                {
                    _consoleService.WriteColumnLine(tokenClaim.Type, 20, tokenClaim.Value);
                }

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

                _consoleService.WriteLine("");
                _consoleService.WriteLine("ACCESS TOKEN");
                _consoleService.WriteLine(_authenticationOptions.Value.AccessToken);


                return true;
            }

            Logger.LogInformation("Access token is INVALID");
        }
        else
        {
            Logger.LogInformation("Access token is not set");
        }

        return false;
    }
}