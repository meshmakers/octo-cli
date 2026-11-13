using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Users;

/// <summary>
///     Creates a user. Runs the CK method <c>System.Identity/User.CreateUser-1</c> (AB#8418, see
///     <see cref="UserPasswordMethodCall" />; no REST fallback); command name, arguments and output are unchanged.
/// </summary>
internal class CreateUser : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _eMailArg;
    private readonly IArgument _nameArg;
    private readonly IArgument _passwordArg;
    private readonly IArgument _passwordStdinArg;
    private readonly SecretInput _secretInput;
    private readonly UserPasswordMethodCall _passwordMethods;

    public CreateUser(ILogger<CreateUser> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService, SecretInput secretInput)
        : base(logger, Constants.IdentityServicesGroup, "CreateUser", "Create a new user account", options,
            identityServicesClient,
            authenticationService)
    {
        _secretInput = secretInput;
        _passwordMethods = new UserPasswordMethodCall(identityServicesClient, tenantClient, authenticationService, logger);
        _eMailArg = CommandArgumentValue.AddArgument("e", "eMail", ["E-Mail of user"],
            true, 1);
        _nameArg = CommandArgumentValue.AddArgument("un", "userName", ["User name"], true,
            1);
        _passwordArg = CommandArgumentValue.AddArgument("p", "password",
            [
                "Deprecated: password of the user. A value on the command line ends up in the shell history and the process list.",
                $"Use --password-stdin or the environment variable {Constants.EnvVarNewPassword} instead; without any of them the user has no password."
            ],
            false, 1);
        _passwordStdinArg = CommandArgumentValue.AddArgument("ps", "password-stdin",
            ["Read the password from standard input (one value; a trailing line break is removed)"], false, 0);
    }


    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_nameArg, "john.doe"),
                    new CodeSampleArgument(_eMailArg, "john.doe@example.com"),
                    new CodeSampleArgument(_passwordStdinArg),
                ],
                    description: "With a password piped in on standard input (e.g. from a secret store)"),
            ],
            Notes:
            [
                $"The password is optional. It is read from standard input (--password-stdin) or from the environment variable {Constants.EnvVarNewPassword}; without either the user is created without a password (there is no prompt). -p still works in this release but logs a deprecation warning, because a value on the command line ends up in the shell history and the process list.",
                $"Runs the CK method System.Identity/User.CreateUser-1 through the asset repository (GraphQL); the password travels only in the request body. Requires identity services {UserPasswordMethodCall.CreateUserMinimumIdentityVersion} or newer with the password methods switched on (Methods:Identity:EnablePasswordMethods); against an older service the command stops with a message naming that version and does not fall back to the REST endpoint. Refusals (e.g. USER_ALREADY_EXISTS, PASSWORD_POLICY_VIOLATION with the policy description) are decided by the server and printed as code and message.",
            ]
        );

    public override async Task Execute()
    {
        var eMail = CommandArgumentValue.GetArgumentScalarValue<string>(_eMailArg).ToLower();
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg).ToLower();
        var password = _secretInput.Resolve(CommandArgumentValue,
            new SecretSpec("password", _passwordArg, _passwordStdinArg, Constants.EnvVarNewPassword, Required: false,
                ConfirmPrompt: false), Logger);

        Logger.LogInformation("Creating user \'{Name}\' at \'{ServiceClientServiceUri}\'", name,
            ServiceClient.ServiceUri);

        await _passwordMethods.CreateUserAsync(name, eMail, password);

        Logger.LogInformation("User \'{Name}\' added", name);
    }
}