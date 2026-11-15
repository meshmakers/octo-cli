using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity;

internal class SetupCommand : Command<OctoToolOptions>
{
    private readonly IArgument _eMailArg;
    private readonly IIdentityServicesSetupClient _identityServicesSetupClient;
    private readonly IArgument _passwordArg;
    private readonly IArgument _passwordStdinArg;
    private readonly SecretInput _secretInput;

    public SetupCommand(ILogger<SetupCommand> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesSetupClient identityServicesSetupClient, SecretInput secretInput)
        : base(logger, Constants.IdentityServicesGroup, "Setup", "Sets identity services up", options)
    {
        _identityServicesSetupClient = identityServicesSetupClient;
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
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_eMailArg, "admin@example.com"),
                ],
                    description: "Interactive: asks for the admin password twice, without echoing it"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_eMailArg, "admin@example.com"),
                    new CodeSampleArgument(_passwordStdinArg),
                ],
                    description: "Automation: pipe the password in (e.g. from a secret store)"),
            ],
            Notes:
            [
                $"The admin password is read from a hidden prompt, from standard input (--password-stdin) or from the environment variable {Constants.EnvVarNewPassword}, so it does not end up in the shell history or the process list. -p/--password is deprecated: it still works in this release but logs a deprecation warning; the date of its removal will be announced.",
            ]
        );

    public override async Task Execute()
    {
        Logger.LogInformation("Adding admin");

        var eMail = CommandArgumentValue.GetArgumentScalarValue<string>(_eMailArg);
        var password = _secretInput.Resolve(CommandArgumentValue,
            new SecretSpec("admin password", _passwordArg, _passwordStdinArg, Constants.EnvVarNewPassword, Required: true,
                ConfirmPrompt: true), Logger)!;

        await _identityServicesSetupClient.AddAdminUser(new AdminUserDto
        {
            EMail = eMail,
            Password = password
        });
    }
}