using System.Net;
using System.Text.RegularExpressions;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity;

internal partial class SetupCommand : Command<OctoToolOptions>
{
    private static readonly string[] SystemTenantNames = ["octosystem", "system"];

    private readonly IAuthenticationService _authenticationService;
    private readonly IOptions<OctoToolAuthenticationOptions> _authenticationOptions;
    private readonly IChildTenantAccess _childTenantAccess;
    private readonly IArgument _eMailArg;
    private readonly IIdentityServicesSetupClient _identityServicesSetupClient;
    private readonly IArgument _passwordArg;
    private readonly IArgument _passwordStdinArg;
    private readonly SecretInput _secretInput;
    private readonly IArgument _tenantIdArg;
    private readonly ITenantSetupClientFactory _tenantSetupClientFactory;

    public SetupCommand(ILogger<SetupCommand> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesSetupClient identityServicesSetupClient, ITenantSetupClientFactory tenantSetupClientFactory,
        IAuthenticationService authenticationService, IOptions<OctoToolAuthenticationOptions> authenticationOptions,
        IChildTenantAccess childTenantAccess, SecretInput secretInput)
        : base(logger, Constants.IdentityServicesGroup, "Setup",
            "Creates the first administrator of the system tenant or of another tenant that has no user yet", options)
    {
        _identityServicesSetupClient = identityServicesSetupClient;
        _tenantSetupClientFactory = tenantSetupClientFactory;
        _authenticationService = authenticationService;
        _authenticationOptions = authenticationOptions;
        _childTenantAccess = childTenantAccess;
        _secretInput = secretInput;

        _eMailArg = CommandArgumentValue.AddArgument("e", "email",
            ["E-Mail of admin"], 1);
        _passwordArg = CommandArgumentValue.AddArgument("p", "password",
            [
                "Deprecated: password of the admin. A value on the command line ends up in the shell history and the process list.",
                $"Omit it to be prompted (hidden input), or use --password-stdin or the environment variable {Constants.EnvVarNewPassword}."
            ],
            false, 1);
        _passwordStdinArg = CommandArgumentValue.AddArgument("ps", "password-stdin",
            ["Read the admin password from standard input (one value; a trailing line break is removed)"], false, 0);
        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId",
            [
                "Tenant whose first administrator is created (default: the system tenant; the tenant of the context is not used).",
                "The system tenant (octosystem) uses the system setup route as before."
            ],
            false, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_eMailArg, "admin@example.com"),
                ],
                    description: "Interactive: asks for the admin password twice, without echoing it (when OCTO_CLI_NEW_PASSWORD is not set)"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_eMailArg, "admin@example.com"),
                    new CodeSampleArgument(_passwordStdinArg),
                ],
                    description: "Automation: pipe the password in (e.g. from a secret store)"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_eMailArg, "admin@example.com"),
                    new CodeSampleArgument(_tenantIdArg, "mytenant"),
                ],
                    description: "First administrator of the child tenant mytenant, run in a client-credentials session (see the notes)"),
            ],
            Notes:
            [
                $"The admin password is read from a hidden prompt, from standard input (--password-stdin) or from the environment variable {Constants.EnvVarNewPassword}, so it does not end up in the shell history or the process list. -p/--password is deprecated: it still works in this release but logs a deprecation warning; the date of its removal will be announced.",
                "Which tenant: without -tid/--tenantId always the system tenant, through the system route without a token, as before, whatever the tenant of the context. Another tenant only with an explicit -tid.",
                $"Another tenant is set up with POST {{tenant}}/v1/setup and a client-credentials token issued for that tenant: the token of the context when it was signed in with LogInClientCredentials on that tenant, else a token requested for the tenant with {Constants.EnvVarClientId} and {Constants.EnvVarClientSecret} (the client that created the tenant, or its mirror there). The service accepts the setup only while the tenant has no user.",
                "A signed-in user is refused for another tenant: the token exchange would create a user in the tenant. An operator gets into a child tenant with Create (which provisions the caller) or ProvisionCurrentUser -ttid <tenant>, then adds local users there with CreateUser.",
            ]
        );

    public override async Task Execute()
    {
        var targetTenantId = ResolveTargetTenantId();
        var tenantSetupClient = targetTenantId == null
            ? null
            : await CreateAuthenticatedTenantSetupClientAsync(targetTenantId);
        var eMail = CommandArgumentValue.GetArgumentScalarValue<string>(_eMailArg);
        var password = _secretInput.Resolve(CommandArgumentValue,
            new SecretSpec("admin password", _passwordArg, _passwordStdinArg, Constants.EnvVarNewPassword, Required: true,
                ConfirmPrompt: true), Logger)!;
        var adminUser = new AdminUserDto
        {
            EMail = eMail,
            Password = password
        };

        if (tenantSetupClient == null)
        {
            Logger.LogInformation("Adding admin to the system tenant");
            await _identityServicesSetupClient.AddAdminUser(adminUser);
            return;
        }

        Logger.LogInformation("Adding admin to tenant '{TenantId}'", targetTenantId);
        try
        {
            await tenantSetupClient.AddAdminUser(adminUser);
        }
        catch (ServiceClientResultException exception) when (exception.HttpStatusCode == HttpStatusCode.NotFound)
        {
            throw new ToolException(
                $"Tenant '{targetTenantId}' already has a user (for example the cross-tenant user of an operator who " +
                "signed in there), or it does not exist, so there is no first administrator to create. An administrator " +
                $"of the tenant adds users with CreateUser; an operator gets in with ProvisionCurrentUser -ttid {targetTenantId}.",
                exception);
        }
    }

    private string? ResolveTargetTenantId()
    {
        if (!CommandArgumentValue.IsArgumentUsed(_tenantIdArg))
        {
            return null;
        }

        var requestedTenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg);
        if (string.IsNullOrWhiteSpace(requestedTenantId))
        {
            throw new ToolException("-tid/--tenantId is empty. Name the tenant, or omit the argument for the system tenant.");
        }

        var normalizedTenantId = requestedTenantId.Trim().ToLowerInvariant();
        if (!TenantIdPattern().IsMatch(normalizedTenantId))
        {
            throw new ToolException(
                $"'{requestedTenantId}' is not a valid tenant id (1 to 64 letters, digits, '-' or '_').");
        }

        var isSystemTenant = SystemTenantNames.Contains(normalizedTenantId);
        return isSystemTenant ? null : normalizedTenantId;
    }

    private async Task<IIdentityServicesSetupClient> CreateAuthenticatedTenantSetupClientAsync(string targetTenantId)
    {
        var authentication = _authenticationOptions.Value;
        var isUserSession = !string.IsNullOrEmpty(authentication.RefreshToken);
        if (isUserSession)
        {
            throw new ToolException(
                $"Setup of tenant '{targetTenantId}' cannot run with a signed-in user: the token exchange creates a " +
                "user in that tenant, and the setup is accepted only while the tenant has no user. As an operator, use " +
                $"Create (it provisions you) or ProvisionCurrentUser -ttid {targetTenantId}, then add users there with " +
                "CreateUser. For an automated setup run it in a client-credentials session: set " +
                $"{Constants.EnvVarClientId} and {Constants.EnvVarClientSecret} to the client that created the tenant " +
                "and run LogInClientCredentials for the parent context.");
        }

        var tenantSetupClient = _tenantSetupClientFactory.Create(targetTenantId);
        var contextTenantId = Options.Value.TenantId?.Trim().ToLowerInvariant();
        var contextTokenTenantId = AccessTokenInspector.TryGetTenantId(authentication.AccessToken)?.ToLowerInvariant();
        var isContextTokenIssuedForTarget = targetTenantId == contextTenantId && targetTenantId == contextTokenTenantId;

        if (isContextTokenIssuedForTarget)
        {
            await _authenticationService.EnsureAuthenticated(tenantSetupClient.AccessToken);
        }
        else
        {
            tenantSetupClient.AccessToken.AccessToken = await _childTenantAccess.GetAccessTokenAsync(targetTenantId);
        }

        if (string.IsNullOrWhiteSpace(tenantSetupClient.AccessToken.AccessToken))
        {
            throw new ToolException(
                $"Setup of tenant '{targetTenantId}' needs a client-credentials token issued for that tenant. Set " +
                $"{Constants.EnvVarClientId} and {Constants.EnvVarClientSecret} to the client that created the tenant " +
                "and run LogInClientCredentials.");
        }

        return tenantSetupClient;
    }

    [GeneratedRegex("^[a-z0-9_-]{1,64}$")]
    private static partial Regex TenantIdPattern();
}
