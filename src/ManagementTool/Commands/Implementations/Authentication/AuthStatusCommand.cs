using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.Frontend.Client.Authentication;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Authentication;

internal class AuthStatusCommand : Command<OctoToolOptions>
{
    private readonly IOptions<OctoToolAuthenticationOptions> _authenticationOptions;
    private readonly IAuthenticationService _authenticationService;
    private readonly IAuthenticatorClient _authenticatorClient;

    public AuthStatusCommand(ILogger<AuthStatusCommand> logger, IOptions<OctoToolOptions> options,
        IOptions<OctoToolAuthenticationOptions> authenticationOptions, IAuthenticatorClient authenticatorClient,
        IAuthenticationService authenticationService)
        : base(logger, "AuthStatus", "Gets authentication status to the configured identity services.", options)
    {
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
            Logger.LogInformation("Refreshing token.");
            var authenticationData =
                await _authenticatorClient.RefreshTokenAsync(_authenticationOptions.Value.RefreshToken);

            _authenticationService.SaveAuthenticationData(authenticationData);

            Logger.LogInformation("Refresh successful. Token expires at \'{AuthenticationDataExpiresAt}\'",
                authenticationData.ExpiresAt);
            await TestAuthenticationStatus();
        }
    }

    private async Task<bool> TestAuthenticationStatus()
    {
        var userInfoData = await _authenticatorClient.GetUserInfoAsync(_authenticationOptions.Value.AccessToken);

        if (userInfoData.IsAuthenticated)
        {
            Logger.LogInformation("Access token is valid.");
            foreach (var claim in userInfoData.Claims)
            {
                Logger.LogInformation("\\t{ClaimType}: {ClaimValue}", claim.Type, claim.Value);
            }

            Logger.LogInformation("\\tAccess Token: {ValueAccessToken}", _authenticationOptions.Value.AccessToken);

            return true;
        }

        Logger.LogInformation("Access token is INVALID");

        return false;
    }
}
