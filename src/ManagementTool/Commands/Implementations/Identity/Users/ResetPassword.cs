using System.Net;
using System.Text.Json;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Users;

/// <summary>
///     Resets the password of a user. The password travels only in the request body (AB#8008). AB#8839: an identity
///     service before release 3.5.55 accepts it only in the request URL and answers the body with a validation error that
///     names <c>userName</c> and <c>password</c> as missing; the command recognizes that answer and stops with a message
///     that names the needed version and the alternatives. It never falls back to the URL form.
/// </summary>
internal class ResetPassword : ServiceClientOctoCommand<IIdentityServicesClient>
{
    /// <summary>The first identity release that takes the new password in the request body (AB#8008).</summary>
    internal const string MinimumIdentityVersion = "3.5.55";

    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _nameArg;
    private readonly IArgument _passwordArg;
    private readonly IArgument _passwordStdinArg;
    private readonly SecretInput _secretInput;
    private readonly IArgument _yesArg;

    public ResetPassword(ILogger<ResetPassword> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService,
        IConfirmationService confirmationService, SecretInput secretInput)
        : base(logger, Constants.IdentityServicesGroup, "ResetPassword", "Resets the password of a user", options,
            identityServicesClient,
            authenticationService)
    {
        _confirmationService = confirmationService;
        _secretInput = secretInput;

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
                "The new password is sent in the request body, never in the request URL. Requires identity services 3.5.55 or newer; they refuse the URL form older octo-cli versions used. An older identity service accepts the password only in the URL: the command then stops with a message naming the needed version and does not fall back to the URL. Until the identity service is updated, the user can reset the password with 'Forgot password' on the sign-in page (needs e-mail delivery).",
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

        try
        {
            await ServiceClient.ResetPassword(name, password);
        }
        catch (ServiceClientResultException e) when (IsBodyFormRefusedByOlderIdentity(e, name, password))
        {
            throw new ServiceClientException(OlderIdentityMessage(ServiceClient.ServiceUri?.ToString()));
        }

        Logger.LogInformation("Resetting password for user \'{Name}\' done", name);
    }

    /// <summary>
    ///     AB#8839: the answer of an identity service before <see cref="MinimumIdentityVersion" /> to the body form - a 400
    ///     validation problem whose <c>errors</c> name both <c>userName</c> and <c>password</c> (it binds both only from the
    ///     query string). The identity service has no version endpoint, so the version is established by elimination: a
    ///     current service validates the body, and its <c>[Required]</c> checks report a field as missing only when the
    ///     value is empty or whitespace. So the answer counts as the older service only when both values sent are
    ///     non-blank; otherwise the original validation errors are reported unchanged.
    /// </summary>
    internal static bool IsBodyFormRefusedByOlderIdentity(ServiceClientResultException exception, string userName,
        string password)
    {
        if (exception.HttpStatusCode != HttpStatusCode.BadRequest ||
            string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        var content = exception.ResponseContent ?? exception.Message;
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("errors", out var errors) ||
                errors.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var fields = errors.EnumerateObject().Select(p => p.Name).ToList();
            return fields.Contains("userName", StringComparer.OrdinalIgnoreCase) &&
                   fields.Contains("password", StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static string OlderIdentityMessage(string? serviceUri) =>
        $"The identity service at '{serviceUri}' is older than {MinimumIdentityVersion}: it accepts a new password only in " +
        "the request URL, where it would end up in request logs, traces and proxy logs, so octo-cli sends it only in the " +
        $"request body (AB#8008). The password was not changed. Update the identity service (OctoMesh {MinimumIdentityVersion} " +
        "or newer); until then the user can reset the password with 'Forgot password' on the sign-in page (needs e-mail " +
        "delivery).";
}
