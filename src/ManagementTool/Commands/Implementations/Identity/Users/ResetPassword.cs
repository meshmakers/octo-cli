using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Users;

/// <summary>
///     Resets the password of a user. Runs the CK method <c>System.Identity/User.ResetPassword-1</c> (AB#8418, see
///     <see cref="UserPasswordMethodCall" />): the password travels only in the GraphQL request body, never in a URL
///     (AB#8008). There is no REST fallback (AB#7039): a server without the method ends the command with a message naming
///     the needed identity version. Command name, arguments, confirmation prompt and output are unchanged.
/// </summary>
internal class ResetPassword : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _nameArg;
    private readonly IArgument _passwordArg;
    private readonly IArgument _passwordStdinArg;
    private readonly UserPasswordMethodCall _passwordMethods;
    private readonly SecretInput _secretInput;
    private readonly IArgument _yesArg;

    public ResetPassword(ILogger<ResetPassword> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService, IConfirmationService confirmationService, SecretInput secretInput)
        : base(logger, Constants.IdentityServicesGroup, "ResetPassword", "Resets the password of a user", options,
            identityServicesClient,
            authenticationService)
    {
        _confirmationService = confirmationService;
        _secretInput = secretInput;
        _passwordMethods = new UserPasswordMethodCall(identityServicesClient, tenantClient, authenticationService, logger);

        _nameArg = CommandArgumentValue.AddArgument("un", "userName", ["User name"], true,
            1);
        _passwordArg = CommandArgumentValue.AddArgument("p", "password",
            [
                "Deprecated: new password of the user. A value on the command line ends up in the shell history and the process list.",
                $"Omit it to be prompted (hidden input), or use --password-stdin or the environment variable {Constants.EnvVarNewPassword}."
            ], false, 1);
        _passwordStdinArg = CommandArgumentValue.AddArgument("ps", "password-stdin",
            ["Read the new password from standard input (one value; a trailing line break is removed)"], false, 0);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_nameArg, "john.doe"),
                ],
                    description: "Interactive: asks for the new password twice, without echoing it"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_nameArg, "john.doe"),
                    new CodeSampleArgument(_passwordStdinArg),
                    new CodeSampleArgument(_yesArg),
                ],
                    description: "Automation: pipe the password in (e.g. from a secret store); -y because a piped standard input cannot confirm"),
            ],
            Notes:
            [
                $"The new password is read from a hidden prompt, from standard input (--password-stdin) or from the environment variable {Constants.EnvVarNewPassword}, so it does not end up in the shell history or the process list. -p still works in this release but logs a deprecation warning and will be removed.",
                "Runs the CK method System.Identity/User.ResetPassword-1 through the asset repository (GraphQL); the new password is sent in the request body, never in a URL. Requires identity services 3.5.52 or newer with the password methods switched on (Methods:Identity:EnablePasswordMethods); against an older service the command stops with a message naming that version and does not fall back to the REST endpoint. Until the service is updated, the user can reset the password with 'Forgot password' on the sign-in page (needs e-mail delivery). Refusals (e.g. PASSWORD_POLICY_VIOLATION with the policy description) are decided by the server and printed as code and message.",
            ]
        );

    public override async Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg).ToLower();
        if (!CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm($"Are you sure you want to reset the password for user '{name}'?"))
        {
            throw ToolException.OperationCancelledByUser();
        }

        var password = _secretInput.Resolve(CommandArgumentValue,
            new SecretSpec("new password", _passwordArg, _passwordStdinArg, Constants.EnvVarNewPassword, Required: true,
                ConfirmPrompt: true), Logger)!;

        Logger.LogInformation("Resetting password for user \'{Name}\' at \'{ServiceClientServiceUri}\'", name,
            ServiceClient.ServiceUri);

        await _passwordMethods.ResetPasswordAsync(name, password);

        Logger.LogInformation("Resetting password for user \'{Name}\' done", name);
    }
}
