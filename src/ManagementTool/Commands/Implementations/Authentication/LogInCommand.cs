﻿using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.Common.Shared;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Authentication;

internal class LogInCommand : Command<OctoToolOptions>
{
    private readonly IAuthenticationService _authenticationService;
    private readonly IAuthenticatorClient _authenticatorClient;
    private readonly IArgument _interactiveArg;

    public LogInCommand(ILogger<LogInCommand> logger, IOptions<OctoToolOptions> options, IAuthenticatorClient authenticatorClient,
        IAuthenticationService authenticationService)
        : base(logger, "LogIn", "LogIn to the configured identity services.", options)
    {
        _authenticatorClient = authenticatorClient;
        _authenticationService = authenticationService;

        _interactiveArg = CommandArgumentValue.AddArgument("i", "interactive",
            new[] { "Interactive by opening a browser for device log-In" }, false);
    }

    public override async Task Execute()
    {
        var isInteractive = CommandArgumentValue.IsArgumentUsed(_interactiveArg);

        Logger.LogInformation("Device log-in at \'{ValueIdentityServiceUrl}\' in progress...", Options.Value.IdentityServiceUrl);

        var apiScopes = CommonConstants.ApiScopes.IdentityApiFullAccess;
        if (!string.IsNullOrWhiteSpace(Options.Value.BotServiceUrl))
        {
            apiScopes |= CommonConstants.ApiScopes.BotApiFullAccess;
        }

        if (!string.IsNullOrWhiteSpace(Options.Value.AssetServiceUrl))
        {
            apiScopes |= CommonConstants.ApiScopes.AssetSystemApiFullAccess;
        }

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
}
