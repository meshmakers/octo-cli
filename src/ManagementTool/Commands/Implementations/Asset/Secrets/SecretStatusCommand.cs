using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Secrets;

/// <summary>
///     Shows the encryption status of Secret attributes (AB#5543, concept §5.2): the environment status, the
///     recent sweep runs with their pre-sweep dump state and the last secret sweep report of the bot service.
///     Read-only; nothing printed ever contains a secret value.
/// </summary>
internal class SecretStatusCommand : ServiceClientOctoCommand<IBotServicesClient>
{
    /// <summary>Number of recent sweep runs printed in tenant mode.</summary>
    internal const int RecentRunLimit = 10;

    private readonly IArgument _allArg;
    private readonly IConsoleService _consoleService;
    private readonly IArgument _jsonArg;
    private readonly IArgument _tenantIdArg;

    public SecretStatusCommand(ILogger<SecretStatusCommand> logger, IOptions<OctoToolOptions> options,
        IBotServicesClient botServicesClient, IAuthenticationService authenticationService,
        IConsoleService consoleService)
        : base(logger, Constants.BotServicesGroup, "SecretStatus",
            "Shows the encryption status of Secret attributes: environment status (key ring, active and known key " +
            "ids, strict mode, recurring Verify), recent sweep runs with their dump state and the last sweep report " +
            "(counts per form and key id, unreadable secrets to re-enter). Never shows secret values.",
            options, botServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId",
            ["Tenant to report on (default: tenant of the context)"], false, 1);
        _allArg = CommandArgumentValue.AddArgument("a", "all",
            ["Show the last report of every tenant (system API, requires system tenant rights)"], false, 0);
        _jsonArg = CommandArgumentValue.AddArgument("j", "json", ["Output as JSON (tenant: environment, recentRuns, report; -a: the reports)"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [], description: "Report of the context tenant"),
                new CodeSample(arguments: [new CodeSampleArgument(_tenantIdArg, "mytenant")],
                    description: "Report of a specific tenant"),
                new CodeSample(arguments: [new CodeSampleArgument(_allArg)],
                    description: "One line per tenant (run against the system tenant)"),
                new CodeSample(arguments: [new CodeSampleArgument(_allArg), new CodeSampleArgument(_jsonArg)],
                    description: "All reports as JSON, e.g. for monitoring scripts"),
            ],
            Notes:
            [
                "Tenant mode prints the environment status (key ring configured, active and known key ids, legacy enc:v1 key, strict mode and since when, recurring Verify cron, this tenant's last Verify), the last 10 sweep runs with the state of their pre-sweep dump, and the last report.",
                "Shows the last report the bot service stored (recurring Verify sweep, a manual ReprotectSecrets run or the sweep after a restore). Run ReprotectSecrets -m Verify -w for a fresh one.",
                "Forms: notSet, plaintext, encV1 (legacy key) and encV2 per key id; unknownKeyId means the value is stored encrypted with a key that is not in the key ring (unreadable, listed as a re-entry task). It becomes readable again when that key is added to the key ring.",
                "Unreadable secrets are removed only by re-entry or ReprotectSecrets -m CleanupUnreadable -y; a pre-sweep dump can be deleted early with DeleteSecretSweepDump.",
            ]);

    public override async Task Execute()
    {
        var all = CommandArgumentValue.IsArgumentUsed(_allArg);
        var json = CommandArgumentValue.IsArgumentUsed(_jsonArg);
        if (all && CommandArgumentValue.IsArgumentUsed(_tenantIdArg))
        {
            throw ToolException.SecretScopeConflict();
        }

        if (all)
        {
            Logger.LogInformation("Reading secret sweep reports of all tenants at '{ServiceClientServiceUri}'",
                ServiceClient.ServiceUri);
            var reports = await ServiceClient.GetSecretSweepReportsAsync();
            if (json)
            {
                _consoleService.WriteLine(JsonConvert.SerializeObject(reports, Formatting.Indented));
                return;
            }

            if (reports.Count == 0)
            {
                Logger.LogInformation("No secret sweep reports yet. Run 'ReprotectSecrets -a -m Verify' to create them");
                return;
            }

            SecretSweepReportPrinter.PrintSummary(Logger, reports.ToList());
            return;
        }

        var tenantId = CommandArgumentValue.IsArgumentUsed(_tenantIdArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg).ToLower()
            : Options.Value.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw ToolException.NoTenantIdConfigured();
        }

        Logger.LogInformation("Reading secret status of tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            tenantId, ServiceClient.ServiceUri);
        var environment = await ServiceClient.GetSecretEnvironmentStatusAsync(tenantId);
        var runs = await ServiceClient.GetSecretSweepRunsAsync(tenantId, RecentRunLimit);
        var report = await ServiceClient.GetSecretSweepReportAsync(tenantId);

        if (json)
        {
            _consoleService.WriteLine(JsonConvert.SerializeObject(
                new { environment, recentRuns = runs, report }, Formatting.Indented));
            return;
        }

        SecretSweepReportPrinter.PrintEnvironment(Logger, environment);
        SecretSweepReportPrinter.PrintRuns(Logger, runs);
        if (report == null)
        {
            Logger.LogInformation(
                "No secret sweep report for tenant '{TenantId}' yet. Run 'ReprotectSecrets -tid {TenantId} -m Verify -w'",
                tenantId, tenantId);
            return;
        }

        SecretSweepReportPrinter.PrintReport(Logger, report);
    }
}
