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
    private readonly IArgument _clientSecretStdinArg;
    private readonly SecretInput _secretInput;

    public LogInClientCredentialsCommand(ILogger<LogInClientCredentialsCommand> logger,
        IOptions<OctoToolOptions> options,
        IAuthenticatorClient authenticatorClient,
        IAuthenticationService authenticationService,
        SecretInput secretInput)
        : base(logger, "LogInClientCredentials",
            $"Non-interactive login using OAuth2 client_credentials. Reads the client ID from -id or {Constants.EnvVarClientId} and the secret from {Constants.EnvVarClientSecret}, standard input (--secret-stdin) or a hidden prompt. Tenant comes from the active context.",
            options)
    {
        _secretInput = secretInput;
        _authenticatorClient = authenticatorClient;
        _authenticationService = authenticationService;

        _clientIdArg = CommandArgumentValue.AddArgument("id", "clientId",
            [$"Client ID. Falls back to env var {Constants.EnvVarClientId}"],
            false, 1);
        _clientSecretArg = CommandArgumentValue.AddArgument("s", "secret",
            [
                "Deprecated: client secret. A value on the command line ends up in the shell history and the process list.",
                $"Omit it to be prompted (hidden input), or use --secret-stdin or the environment variable {Constants.EnvVarClientSecret}."
            ], false, 1);
        _clientSecretStdinArg = CommandArgumentValue.AddArgument("ss", "secret-stdin",
            ["Read the client secret from standard input (one value; a trailing line break is removed)"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [], description: "Headless run — export credentials, then login"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_clientIdArg, "my-script-client"),
                    new CodeSampleArgument(_clientSecretStdinArg),
                ],
                    description: "Or pipe the secret in on standard input (e.g. from a secret store); -id takes precedence over the env var"),
            ],
            Notes:
            [
                $"Secret sources, first match wins: --secret-stdin, {Constants.EnvVarClientSecret}, a hidden prompt (only on a terminal). -s still works in this release but logs a deprecation warning and will be removed, because a value on the command line ends up in the shell history and the process list.",
                "Tenant comes from the active context. Switch contexts (e.g. `octo-cli -c UseContext -n prod`) to target a different tenant. The login fails fast if the active context has no `TenantId`.",
                "The client is per-tenant. If a script needs to address multiple tenants, create one `client_credentials` client per tenant and switch contexts before each login.",
                "No refresh token is issued (per OAuth2 spec for `client_credentials`). While `OCTO_CLI_CLIENT_ID` and `OCTO_CLI_CLIENT_SECRET` remain set in the environment, octo-cli automatically re-acquires the token when it expires — long-running scripts do not need to call `LogInClientCredentials` again between commands. If the env vars are unset, the next API call after expiry returns 401 and you must re-run the login.",
                "The token is stored in the active context's `Authentication` block, exactly like a device-code token.",
            ]
        );

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
        var clientSecret = _secretInput.Resolve(CommandArgumentValue,
            new SecretSpec("client secret", _clientSecretArg, _clientSecretStdinArg, Constants.EnvVarClientSecret,
                Required: true, ConfirmPrompt: false), Logger)!;

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
