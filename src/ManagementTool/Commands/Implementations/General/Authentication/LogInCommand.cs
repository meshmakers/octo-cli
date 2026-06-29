using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.Communication.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.General.Authentication;

internal class LogInCommand : Command<OctoToolOptions>
{
    private readonly IOptions<OctoToolAuthenticationOptions> _authenticationOptions;
    private readonly IAuthenticationService _authenticationService;
    private readonly IAuthenticatorClient _authenticatorClient;
    private readonly IArgument _ifNeededArg;
    private readonly IArgument _interactiveArg;

    public LogInCommand(ILogger<LogInCommand> logger, IOptions<OctoToolOptions> options,
        IOptions<OctoToolAuthenticationOptions> authenticationOptions,
        IAuthenticatorClient authenticatorClient,
        IAuthenticationService authenticationService)
        : base(logger, "LogIn", "LogIn to the configured identity services.", options)
    {
        _authenticationOptions = authenticationOptions;
        _authenticatorClient = authenticatorClient;
        _authenticationService = authenticationService;

        _interactiveArg = CommandArgumentValue.AddArgument("i", "interactive",
            ["Interactive by opening a browser for device log-In"], false);
        _ifNeededArg = CommandArgumentValue.AddArgument("in", "if-needed",
            [
                "Skip the login when the stored token is still valid or can be refreshed silently;",
                "only fall back to a device log-in when neither is possible"
            ], false);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_interactiveArg)], description: "Basic usage"),
                new CodeSample(
                    arguments: [new CodeSampleArgument(_interactiveArg), new CodeSampleArgument(_ifNeededArg)],
                    description: "Non-disruptive: only opens a browser if the token cannot be refreshed"),
            ],
            Notes:
            [
                "- `--if-needed` (`-in`) first checks whether the current access token is still valid; if not, it tries a silent refresh-token exchange. A device log-in is only started when both fail. Combine with `-i` so the browser opens automatically in that fallback case. Ideal for setup scripts that re-run often."
            ]
        );

    public override async Task Execute()
    {
        var isInteractive = CommandArgumentValue.IsArgumentUsed(_interactiveArg);
        var ifNeeded = CommandArgumentValue.IsArgumentUsed(_ifNeededArg);

        // --if-needed: avoid the disruptive device flow when the existing session
        // is still usable (valid access token) or can be renewed silently via the
        // refresh token. Only continue to the device flow when neither works.
        if (ifNeeded && await TryReuseOrRefreshSession())
        {
            return;
        }

        Logger.LogInformation("Device log-in at \'{ValueIdentityServiceUrl}\' in progress...",
            Options.Value.IdentityServiceUrl);

        var apiScopes = ApiScopes.OctoApiFullAccess;

        var response = await _authenticatorClient.RequestDeviceAuthorizationAsync(apiScopes);

        Logger.LogInformation("Device Code: {ResponseDeviceCode}", response.DeviceCode);
        Logger.LogInformation("Estimated code expiration at: {ResponseExpiresAt}", response.ExpiresAt);
        Logger.LogInformation("");
        Logger.LogInformation("");
        Logger.LogInformation("Using a browser, visit:");
        Logger.LogInformation("{ResponseVerificationUri}", response.VerificationUri);
        Logger.LogInformation("Enter the code:");
        Logger.LogInformation("{ResponseUserCode}", response.UserCode);

        if (isInteractive)
        {
            Logger.LogInformation("Opening default browser...");
            if (response.VerificationUriComplete != null)
            {
                Process.Start(new ProcessStartInfo(response.VerificationUriComplete) { UseShellExecute = true });
            }
        }


        while (true)
        {
            Logger.LogInformation("Waiting for device authentication...");
            Thread.Sleep(response.PollingInterval * 1000);

            if (response.DeviceCode != null)
            {
                var authenticationData = await _authenticatorClient.RequestDeviceTokenAsync(response.DeviceCode);
                if (authenticationData.IsAuthenticationPending)
                {
                    Thread.Sleep(response.PollingInterval * 1000);
                    continue;
                }

                _authenticationService.SaveAuthenticationData(authenticationData);

                Logger.LogInformation("Device log-in successful. Token expires at \'{AuthenticationDataExpiresAt}\'",
                    authenticationData.ExpiresAt);
            }

            break;
        }
    }

    /// <summary>
    ///     Tries to satisfy the login without a device flow: reuse a still-valid access token, otherwise
    ///     exchange the refresh token for a fresh one. Returns true when the session is usable afterwards.
    /// </summary>
    private async Task<bool> TryReuseOrRefreshSession()
    {
        if (IsAccessTokenValid(_authenticationOptions.Value.AccessToken))
        {
            Logger.LogInformation("Existing access token is still valid; skipping device log-in.");
            return true;
        }

        var refreshToken = _authenticationOptions.Value.RefreshToken;
        if (string.IsNullOrEmpty(refreshToken))
        {
            Logger.LogInformation("No valid access token and no refresh token available; device log-in required.");
            return false;
        }

        try
        {
            var authenticationData = await _authenticatorClient.RefreshTokenAsync(refreshToken);
            _authenticationService.SaveAuthenticationData(authenticationData);
            Logger.LogInformation(
                "Access token refreshed silently. Token expires at \'{AuthenticationDataExpiresAt}\'",
                authenticationData.ExpiresAt);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogInformation("Token refresh failed ({Reason}); falling back to device log-in.", ex.Message);
            return false;
        }
    }

    /// <summary>
    ///     Returns true when the access token parses as a JWT and is not expired (with a 30 second skew, so a
    ///     token about to expire is treated as needing a refresh).
    /// </summary>
    private static bool IsAccessTokenValid(string? accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return false;
        }

        try
        {
            var token = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
            // ValidTo is UTC by JwtSecurityToken convention; require a real, still-future expiry.
            return token.ValidTo != default && token.ValidTo > DateTime.UtcNow.AddSeconds(30);
        }
        catch
        {
            return false;
        }
    }
}
