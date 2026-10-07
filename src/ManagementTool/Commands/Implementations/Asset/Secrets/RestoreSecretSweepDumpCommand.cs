using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Secrets;

/// <summary>
///     Restores the pre-sweep dump of a secret sweep run into the tenant it was taken from (AB#5559). The tenant's
///     database is replaced by the dump and a Verify runs afterwards. A dump taken before the first Encrypt holds
///     plaintext secrets, so the restore brings the plaintext back: run Encrypt afterwards. Requires the
///     SecretManagement role.
/// </summary>
internal class RestoreSecretSweepDumpCommand : JobWithWaitOctoCommand
{
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _runIdArg;
    private readonly IArgument _tenantIdArg;
    private readonly IArgument _yesArg;

    public RestoreSecretSweepDumpCommand(ILogger<RestoreSecretSweepDumpCommand> logger,
        IOptions<OctoToolOptions> options, IBotServicesClient botServicesClient,
        IAuthenticationService authenticationService, IConfirmationService confirmationService)
        : base(logger, Constants.BotServicesGroup, "RestoreSecretSweepDump",
            "Restores the pre-sweep dump of a secret sweep run into the same tenant, replacing its data " +
            "(requires SecretManagement). Use -y to skip confirmation, -w to wait.",
            options, botServicesClient, authenticationService)
    {
        _confirmationService = confirmationService;
        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId",
            ["Tenant of the sweep run; the dump is restored into it (default: tenant of the context)"], false, 1);
        _runIdArg = CommandArgumentValue.AddArgument("r", "runId",
            ["Id of the sweep run whose pre-sweep dump is restored (see SecretStatus)"], true, 1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_tenantIdArg, "mytenant"),
                    new CodeSampleArgument(_runIdArg, "1234"),
                    new CodeSampleArgument(_waitForJobArg),
                ],
                    description: "Roll a tenant back to the state before a sweep and wait for the restore"),
            ],
            Notes:
            [
                "The tenant's database is dropped and replaced by the dump; all changes since the sweep run are lost. A Verify runs after the restore.",
                "A dump taken before the first Encrypt contains plaintext secrets: restoring it brings the plaintext back. Run 'ReprotectSecrets -m Encrypt' afterwards.",
                "Refused when the dump was deleted or expired, no longer exists, or is encrypted with a key id that is not in the key ring (DumpKeyMissing; SecretStatus lists the key ids the dumps need).",
                "Without -w the command prints the job id and returns.",
            ]);

    public override async Task Execute()
    {
        var runId = CommandArgumentValue.GetArgumentScalarValue<string>(_runIdArg).Trim();

        var tenantId = CommandArgumentValue.IsArgumentUsed(_tenantIdArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg).ToLower()
            : Options.Value.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw ToolException.NoTenantIdConfigured();
        }

        if (!CommandArgumentValue.IsArgumentUsed(_yesArg) &&
            !_confirmationService.Confirm(
                $"restore the pre-sweep dump of sweep run '{runId}' into tenant '{tenantId}'? The tenant's data is " +
                "replaced by the dump; a dump taken before the first Encrypt brings plaintext secrets back"))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation("Restoring the pre-sweep dump of sweep run '{RunId}' into tenant '{TenantId}' at " +
                              "'{ServiceClientServiceUri}'", runId, tenantId, ServiceClient.ServiceUri);
        string jobId;
        try
        {
            // Reaching this point means the operator confirmed (prompt or -y).
            var job = await ServiceClient.RestoreSecretSweepDumpAsync(tenantId, runId, true);
            jobId = job.JobId;
        }
        catch (SecretSweepDumpRestoreException ex)
        {
            throw ToolException.SecretSweepDumpRestoreRefused(tenantId, runId, ex.Reason);
        }

        Logger.LogInformation("Restore job '{JobId}' has been started", jobId);

        if (!CommandArgumentValue.IsArgumentUsed(_waitForJobArg))
        {
            Logger.LogInformation("Use -w to wait for completion; afterwards check SecretStatus and run " +
                                  "'ReprotectSecrets -m Encrypt' if the dump predates the first Encrypt");
            return;
        }

        await WaitForJob(jobId);
        Logger.LogInformation("Pre-sweep dump of sweep run '{RunId}' restored into tenant '{TenantId}'. Check " +
                              "SecretStatus and run 'ReprotectSecrets -tid {TenantId} -m Encrypt' if the dump " +
                              "predates the first Encrypt", runId, tenantId, tenantId);
    }
}
