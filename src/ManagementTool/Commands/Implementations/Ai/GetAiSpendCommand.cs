using System.Globalization;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AiServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Ai;

/// <summary>
///     Shows key source, monthly LLM cap and spend (current and previous UTC month) of the central-key LLM gateway
///     (AB#8690). Without <c>-t</c> it reads the context's own tenant (read-only for every user with read access); with
///     <c>-t</c> it reads another tenant through the cap administration (system tenant, role <c>AiBudgetAdmin</c>).
/// </summary>
internal class GetAiSpendCommand : ServiceClientOctoCommand<IAiServicesClient>
{
    public const string CommandName = "GetAiSpend";
    private const int LabelWidth = 22;

    private readonly IConsoleService _consoleService;
    private readonly IArgument _tenantArg;

    public GetAiSpendCommand(ILogger<GetAiSpendCommand> logger, IConsoleService consoleService,
        IOptions<OctoToolOptions> options, IAiServicesClient aiServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AiServicesGroup, "GetAiSpend", // literal for the command-reference generator
            "Shows the monthly LLM spend cap, the key source and the spend (spent, reserved, calls, reset time) of the " +
            "current and previous UTC month of the central-key LLM gateway.",
            options, aiServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _tenantArg = CommandArgumentValue.AddArgument("t", "tenantId",
            ["Tenant to read as platform admin (system tenant, role AiBudgetAdmin). Default: the context's own tenant"],
            false, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [], description: "Cap and spend of the context's tenant"),
                new CodeSample(arguments: [new CodeSampleArgument(_tenantArg, "ai-sandbox")],
                    description: "Cap and spend of another tenant (system tenant, role AiBudgetAdmin)")
            ],
            Notes:
            [
                "A tenant without a cap reads as key source None: the gateway refuses its central-key calls.",
                "Spent and reserved are the amounts the gateway enforces (its ledger); the reset is the start of the next " +
                "UTC month."
            ]);

    public override async Task Execute()
    {
        AiSpendReportDto report;
        if (CommandArgumentValue.IsArgumentUsed(_tenantArg))
        {
            var tenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantArg).Trim();
            Logger.LogInformation("Reading the LLM spend of tenant '{TenantId}' as platform admin at '{ServiceClientServiceUri}'",
                tenantId, ServiceClient.ServiceUri);
            report = await ServiceClient.GetAiSpendCapAsync(tenantId);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
            {
                Logger.LogError("TenantId is missing");
                return;
            }

            report = await ServiceClient.GetAiSpendAsync();
        }

        Write(report);
    }

    private void Write(AiSpendReportDto report)
    {
        _consoleService.WriteColumnLine("Tenant", LabelWidth, report.TenantId);
        _consoleService.WriteColumnLine("Key source", LabelWidth, report.KeySource);
        _consoleService.WriteColumnLine("Cap (EUR/month)", LabelWidth, Eur(report.CapMonthlyEur));
        foreach (var month in report.Months)
        {
            _consoleService.WriteColumnLine($"{month.Month} spent", LabelWidth, Eur(month.SpentEur));
            _consoleService.WriteColumnLine($"{month.Month} reserved", LabelWidth, Eur(month.ReservedEur));
            _consoleService.WriteColumnLine($"{month.Month} calls", LabelWidth,
                month.CallCount.ToString(CultureInfo.InvariantCulture));
            _consoleService.WriteColumnLine($"{month.Month} resets", LabelWidth,
                month.ResetsAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));
        }
    }

    private static string Eur(decimal value) => value.ToString("0.00####", CultureInfo.InvariantCulture);
}
