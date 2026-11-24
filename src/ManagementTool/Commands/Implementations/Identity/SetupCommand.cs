using System.Text.RegularExpressions;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity;

internal partial class SetupCommand : Command<OctoToolOptions>
{
    private static readonly string[] SystemTenantNames = ["octosystem", "system"];

    private readonly IAuthenticationService _authenticationService;
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
        IAuthenticationService authenticationService, IChildTenantAccess childTenantAccess, SecretInput secretInput)
        : base(logger, Constants.IdentityServicesGroup, "Setup",
            "Creates the first administrator of the system tenant or of another tenant that has no user yet", options)
    {
        _identityServicesSetupClient = identityServicesSetupClient;
        _tenantSetupClientFactory = tenantSetupClientFactory;
        _authenticationService = authenticationService;
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
                "Tenant whose first administrator is created (default: tenant of the context).",
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
                    description: "First administrator of the child tenant mytenant (needs a token for mytenant, see the notes)"),
            ],
            Notes:
            [
                $"The admin password is read from a hidden prompt, from standard input (--password-stdin) or from the environment variable {Constants.EnvVarNewPassword}, so it does not end up in the shell history or the process list. -p/--password is deprecated: it still works in this release but logs a deprecation warning; the date of its removal will be announced.",
                "Which tenant: -tid/--tenantId when given, else the tenant of the context. The system tenant (octosystem, also when it is the tenant of the context) is set up through the system route without a token, as before.",
                "Another tenant is set up with POST {tenant}/v1/setup and a token issued for that tenant: the token of the context when the tenant is the context's own (exchanged when the context was signed in on a parent tenant), else a token obtained for the child tenant (client credentials of its creator or mirror, or the exchanged token of the signed-in user). The service accepts the call only while the tenant has no user.",
            ]
        );

    public override async Task Execute()
    {
        var targetTenantId = ResolveTargetTenantId();
        var eMail = CommandArgumentValue.GetArgumentScalarValue<string>(_eMailArg);
        var password = _secretInput.Resolve(CommandArgumentValue,
            new SecretSpec("admin password", _passwordArg, _passwordStdinArg, Constants.EnvVarNewPassword, Required: true,
                ConfirmPrompt: true), Logger)!;
        var adminUser = new AdminUserDto
        {
            EMail = eMail,
            Password = password
        };

        if (targetTenantId == null)
        {
            Logger.LogInformation("Adding admin to the system tenant");
            await _identityServicesSetupClient.AddAdminUser(adminUser);
            return;
        }

        var tenantSetupClient = await CreateAuthenticatedTenantSetupClientAsync(targetTenantId);
        Logger.LogInformation("Adding admin to tenant '{TenantId}'", targetTenantId);
        await tenantSetupClient.AddAdminUser(adminUser);
    }

    private string? ResolveTargetTenantId()
    {
        var isTenantArgumentUsed = CommandArgumentValue.IsArgumentUsed(_tenantIdArg);
        var requestedTenantId = isTenantArgumentUsed
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg)
            : Options.Value.TenantId;

        if (string.IsNullOrWhiteSpace(requestedTenantId))
        {
            return null;
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
        var tenantSetupClient = _tenantSetupClientFactory.Create(targetTenantId);
        var contextTenantId = Options.Value.TenantId?.Trim().ToLowerInvariant();
        var isContextTenant = targetTenantId == contextTenantId;

        if (isContextTenant)
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
                $"Setting up tenant '{targetTenantId}' needs a token issued for that tenant. Sign in to a context of " +
                "the tenant or its parent (LogIn, or LogInClientCredentials with the client that created the tenant).");
        }

        return tenantSetupClient;
    }

    [GeneratedRegex("^[a-z0-9_-]{1,64}$")]
    private static partial Regex TenantIdPattern();
}
