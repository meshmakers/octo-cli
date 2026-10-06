using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Secrets;

/// <summary>
///     Starts a secret sweep job in the bot service (AB#5543, concept §5.2): re-protect all Secret attributes
///     with the active key (default), encrypt remaining legacy values, remove values whose key id is not in the
///     key ring (CleanupUnreadable, needs -y), or only verify. Writing modes ask for confirmation and send
///     confirm=true to the bot service; the emergency Decrypt mode is not offered.
/// </summary>
internal class ReprotectSecretsCommand : JobWithWaitOctoCommand
{
    private readonly IArgument _allArg;
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _modeArg;
    private readonly IArgument _tenantIdArg;
    private readonly IArgument _yesArg;

    public ReprotectSecretsCommand(ILogger<ReprotectSecretsCommand> logger, IOptions<OctoToolOptions> options,
        IBotServicesClient botServicesClient, IAuthenticationService authenticationService,
        IConfirmationService confirmationService)
        : base(logger, Constants.BotServicesGroup, "ReprotectSecrets",
            "Starts a secret sweep job: re-encrypts all Secret attributes with the active key (Reprotect, default), " +
            "encrypts remaining legacy values (Encrypt), removes values whose key id is not in the key ring " +
            "(CleanupUnreadable, requires -y) or only counts them (Verify). Use -w to wait, -y to skip confirmation.",
            options, botServicesClient, authenticationService)
    {
        _confirmationService = confirmationService;
        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId",
            ["Tenant to sweep (default: tenant of the context)"], false, 1);
        _allArg = CommandArgumentValue.AddArgument("a", "all",
            ["Sweep all tenants (system API, requires system tenant rights)"], false, 0);
        _modeArg = CommandArgumentValue.AddArgument("m", "mode",
            ["Sweep mode: Reprotect (default), Encrypt, CleanupUnreadable or Verify"], false, 1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_waitForJobArg)],
                    description: "Re-protect the context tenant after a key rotation and wait for the report"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_tenantIdArg, "mytenant"),
                    new CodeSampleArgument(_modeArg, "Verify"),
                    new CodeSampleArgument(_waitForJobArg),
                ],
                    description: "Count the forms of all secrets of a tenant (read-only, no confirmation)"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_allArg),
                    new CodeSampleArgument(_modeArg, "Encrypt"),
                    new CodeSampleArgument(_yesArg),
                ],
                    description: "Encrypt remaining plaintext / enc:v1 values in all tenants (CI/CD)"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_tenantIdArg, "mytenant"),
                    new CodeSampleArgument(_modeArg, "CleanupUnreadable"),
                    new CodeSampleArgument(_yesArg),
                    new CodeSampleArgument(_waitForJobArg),
                ],
                    description: "Remove secrets that cannot be read with this key ring (after a restore, once they were re-entered or are not needed)"),
            ],
            Notes:
            [
                "Writing modes (Reprotect, Encrypt, CleanupUnreadable) take a pre-sweep dump in the bot service and ask for confirmation; -y skips it. The CLI sends confirm=true to the bot service once confirmed.",
                "CleanupUnreadable permanently removes secrets whose key id is not in the key ring (e.g. after a restore from another environment) and requires -y; recoverable only from the pre-sweep dump. SecretStatus lists these secrets as unreadable (re-entry tasks) before.",
                "Restore with the source environment's key: add the key id to the key ring, then run Reprotect to move the values to the active key, then remove the source key.",
                "Decrypt (writes clear text back) is an emergency operation and is not available in the CLI.",
                "Without -w the command prints the job id and returns; follow up with SecretStatus.",
            ]);

    public override async Task Execute()
    {
        var all = CommandArgumentValue.IsArgumentUsed(_allArg);
        if (all && CommandArgumentValue.IsArgumentUsed(_tenantIdArg))
        {
            throw ToolException.SecretScopeConflict();
        }

        var mode = SecretSweepReportPrinter.ParseMode(
            CommandArgumentValue.IsArgumentUsed(_modeArg)
                ? CommandArgumentValue.GetArgumentScalarValue<string>(_modeArg)
                : null,
            SecretSweepModeDto.Reprotect);

        string? tenantId = null;
        if (!all)
        {
            tenantId = CommandArgumentValue.IsArgumentUsed(_tenantIdArg)
                ? CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg).ToLower()
                : Options.Value.TenantId;
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                throw ToolException.NoTenantIdConfigured();
            }
        }

        var scope = all ? "all tenants" : $"tenant '{tenantId}'";
        var yes = CommandArgumentValue.IsArgumentUsed(_yesArg);
        if (mode == SecretSweepModeDto.CleanupUnreadable && !yes)
        {
            throw ToolException.SecretCleanupRequiresYes();
        }

        if (mode != SecretSweepModeDto.Verify && !yes &&
            !_confirmationService.Confirm(ConfirmationMessage(mode, scope)))
        {
            throw ToolException.OperationCancelledByUser();
        }

        // Reaching this point for a writing mode means the operator confirmed (prompt or -y).
        var confirm = mode != SecretSweepModeDto.Verify;

        Logger.LogInformation("Starting {Mode} secret sweep for {Scope} at '{ServiceClientServiceUri}'", mode, scope,
            ServiceClient.ServiceUri);
        var job = all
            ? await ServiceClient.StartSecretSweepAllTenantsAsync(mode, confirm)
            : await ServiceClient.StartSecretSweepAsync(tenantId!, mode, confirm);
        Logger.LogInformation("Secret sweep job '{JobId}' has been started", job.JobId);

        if (!CommandArgumentValue.IsArgumentUsed(_waitForJobArg))
        {
            Logger.LogInformation("Use -w to wait for completion, or check the result later with SecretStatus");
            return;
        }

        await WaitForJob(job.JobId);

        if (all)
        {
            var reports = await ServiceClient.GetSecretSweepReportsAsync();
            SecretSweepReportPrinter.PrintSummary(Logger, reports.ToList());
            return;
        }

        var report = await ServiceClient.GetSecretSweepReportAsync(tenantId!);
        if (report != null)
        {
            SecretSweepReportPrinter.PrintReport(Logger, report);
        }
    }

    private static string ConfirmationMessage(SecretSweepModeDto mode, string scope) => mode switch
    {
        SecretSweepModeDto.Encrypt =>
            $"encrypt all remaining plaintext / enc:v1 secrets of {scope} with the active key? Older binaries " +
            "cannot read the result",
        _ => $"re-encrypt all secrets of {scope} with the active key? Every stored secret is rewritten (a " +
             "pre-sweep dump is taken first)"
    };
}
