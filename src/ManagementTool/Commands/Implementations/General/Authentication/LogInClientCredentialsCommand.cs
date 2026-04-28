using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.Communication.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.General.Authentication;

internal class LogInClientCredentialsCommand : Command<OctoToolOptions>
{
    private readonly IAuthenticatorClient _authenticatorClient;
    private readonly IAuthenticationService _authenticationService;
    private readonly IArgument _clientIdArg;
    private readonly IArgument _clientSecretArg;

    public LogInClientCredentialsCommand(ILogger<LogInClientCredentialsCommand> logger,
        IOptions<OctoToolOptions> options,
        IAuthenticatorClient authenticatorClient,
        IAuthenticationService authenticationService)
        : base(logger, "LogInClientCredentials",
            $"Non-interactive login using OAuth2 client_credentials. Reads credentials from -id/-s arguments or {Constants.EnvVarClientId}/{Constants.EnvVarClientSecret} env vars. Tenant comes from the active context.",
            options)
    {
        _authenticatorClient = authenticatorClient;
        _authenticationService = authenticationService;

        _clientIdArg = CommandArgumentValue.AddArgument("id", "clientId",
            [$"Client ID. Falls back to env var {Constants.EnvVarClientId}"],
            false, 1);
        _clientSecretArg = CommandArgumentValue.AddArgument("s", "secret",
            [$"Client secret. Falls back to env var {Constants.EnvVarClientSecret}"],
            false, 1);
    }

    public override async Task Execute()
    {
        var tenantId = Options.Value.TenantId;
        if (string.IsNullOrEmpty(tenantId))
        {
            throw new ToolException(
                "Active context has no TenantId — set one with 'octo-cli -c Config -tid <id>'.");
        }

        var clientId = ResolveArg(_clientIdArg, Constants.EnvVarClientId,
            $"Client ID required. Provide via -id or {Constants.EnvVarClientId}.");
        var clientSecret = ResolveArg(_clientSecretArg, Constants.EnvVarClientSecret,
            $"Client secret required. Provide via -s or {Constants.EnvVarClientSecret}.");

        Logger.LogInformation(
            "Client-credentials log-in at '{IdentityServiceUrl}' for tenant '{TenantId}' as client '{ClientId}'...",
            Options.Value.IdentityServiceUrl, tenantId, clientId);

        var authenticationData = await _authenticatorClient.RequestClientCredentialsTokenAsync(
            ApiScopes.OctoApiFullAccess,
            DefaultScopes.None,
            customScopes: null,
            clientId: clientId,
            clientSecret: clientSecret);

        _authenticationService.SaveAuthenticationData(authenticationData);

        Logger.LogInformation(
            "Client-credentials log-in successful. Token expires at '{ExpiresAt}'. No refresh token is issued; "
            + $"set {Constants.EnvVarClientId} / {Constants.EnvVarClientSecret} in the environment to enable automatic re-acquisition.",
            authenticationData.ExpiresAt);
    }

    private string ResolveArg(IArgument argument, string envVarName, string missingMessage)
    {
        if (CommandArgumentValue.IsArgumentUsed(argument))
        {
            var argValue = CommandArgumentValue.GetArgumentScalarValue<string>(argument);
            if (!string.IsNullOrEmpty(argValue))
            {
                return argValue;
            }
        }

        var envValue = Environment.GetEnvironmentVariable(envVarName);
        if (!string.IsNullOrEmpty(envValue))
        {
            return envValue;
        }

        throw new ToolException(missingMessage);
    }
}
