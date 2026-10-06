using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Secrets;

/// <summary>
///     Shows the encryption status of Secret attributes from the last secret sweep report of the bot service
///     (AB#5543, concept §5.2). Read-only; the report never contains secret values.
/// </summary>
internal class SecretStatusCommand : ServiceClientOctoCommand<IBotServicesClient>
{
    private readonly IArgument _allArg;
    private readonly IConsoleService _consoleService;
    private readonly IArgument _jsonArg;
    private readonly IArgument _tenantIdArg;

    public SecretStatusCommand(ILogger<SecretStatusCommand> logger, IOptions<OctoToolOptions> options,
        IBotServicesClient botServicesClient, IAuthenticationService authenticationService,
        IConsoleService consoleService)
        : base(logger, Constants.BotServicesGroup, "SecretStatus",
            "Shows the encryption status of Secret attributes from the last secret sweep report (counts per form " +
            "and key id, strict mode, secrets to re-enter). Never shows secret values.",
            options, botServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId",
            ["Tenant to report on (default: tenant of the context)"], false, 1);
        _allArg = CommandArgumentValue.AddArgument("a", "all",
            ["Show the last report of every tenant (system API, requires system tenant rights)"], false, 0);
        _jsonArg = CommandArgumentValue.AddArgument("j", "json", ["Output the raw report(s) as JSON"], false, 0);
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
                "Shows the last report the bot service stored (recurring Verify sweep, a manual ReprotectSecrets run or the sweep after a restore). Run ReprotectSecrets -m Verify -w for a fresh one.",
                "Forms: notSet, placeholder, plaintext, encV1 (legacy key) and encV2 per key id; unknownKeyId means the value was encrypted with a key that is not in the key ring.",
                "Secrets to re-enter are values cleared because of an unknown key id (cross-environment restore); set them again in Studio or via the API.",
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

        Logger.LogInformation("Reading secret sweep report of tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            tenantId, ServiceClient.ServiceUri);
        var report = await ServiceClient.GetSecretSweepReportAsync(tenantId);
        if (report == null)
        {
            Logger.LogInformation(
                "No secret sweep report for tenant '{TenantId}' yet. Run 'ReprotectSecrets -tid {TenantId} -m Verify -w'",
                tenantId, tenantId);
            return;
        }

        if (json)
        {
            _consoleService.WriteLine(JsonConvert.SerializeObject(report, Formatting.Indented));
            return;
        }

        SecretSweepReportPrinter.PrintReport(Logger, report);
    }
}
