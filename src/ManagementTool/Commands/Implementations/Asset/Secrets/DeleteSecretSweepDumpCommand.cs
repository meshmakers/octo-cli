using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Secrets;

/// <summary>
///     Deletes the pre-sweep dump of a secret sweep run before it expires (AB#5543, contract §9). Dumps are never
///     downloadable; deleting one early removes the only way back after an Encrypt / CleanupUnreadable sweep.
///     Requires the SecretManagement role.
/// </summary>
internal class DeleteSecretSweepDumpCommand : ServiceClientOctoCommand<IBotServicesClient>
{
    private readonly IConfirmationService _confirmationService;
    private readonly IArgument _runIdArg;
    private readonly IArgument _tenantIdArg;
    private readonly IArgument _yesArg;

    public DeleteSecretSweepDumpCommand(ILogger<DeleteSecretSweepDumpCommand> logger,
        IOptions<OctoToolOptions> options, IBotServicesClient botServicesClient,
        IAuthenticationService authenticationService, IConfirmationService confirmationService)
        : base(logger, Constants.BotServicesGroup, "DeleteSecretSweepDump",
            "Deletes the pre-sweep dump of a secret sweep run before it expires (requires SecretManagement). " +
            "Use -y to skip confirmation.",
            options, botServicesClient, authenticationService)
    {
        _confirmationService = confirmationService;
        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId",
            ["Tenant of the sweep run (default: tenant of the context)"], false, 1);
        _runIdArg = CommandArgumentValue.AddArgument("r", "runId",
            ["Id of the sweep run (see SecretStatus)"], true, 1);
        _yesArg = CommandArgumentValue.AddArgument("y", "yes", ["Skip confirmation prompt"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_tenantIdArg, "mytenant"),
                    new CodeSampleArgument(_runIdArg, "1234"),
                ],
                    description: "Delete the dump of a sweep run after checking the result"),
            ],
            Notes:
            [
                "Dumps are kept for 7 days and then expire automatically; SecretStatus lists the runs and the state of their dump.",
                "After the dump is deleted, values removed by CleanupUnreadable cannot be recovered any more.",
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
                $"delete the pre-sweep dump of sweep run '{runId}' of tenant '{tenantId}'? Values removed by the " +
                "sweep cannot be recovered afterwards"))
        {
            throw ToolException.OperationCancelledByUser();
        }

        Logger.LogInformation("Deleting the pre-sweep dump of sweep run '{RunId}' of tenant '{TenantId}' at " +
                              "'{ServiceClientServiceUri}'", runId, tenantId, ServiceClient.ServiceUri);
        var result = await ServiceClient.DeleteSecretSweepDumpAsync(tenantId, runId);
        switch (result)
        {
            case SecretSweepDumpDeleteResultDto.Deleted:
                Logger.LogInformation("Pre-sweep dump of sweep run '{RunId}' deleted", runId);
                break;
            case SecretSweepDumpDeleteResultDto.AlreadyDeleted:
                Logger.LogWarning("The pre-sweep dump of sweep run '{RunId}' was already deleted", runId);
                break;
            default:
                throw ToolException.SecretSweepDumpNotFound(tenantId, runId);
        }
    }
}
