using System.Globalization;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AiServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Ai;

/// <summary>
///     Sets the monthly LLM spend cap (and key source) of a tenant for the central-key LLM gateway (AB#8690). Needs a
///     login to the system tenant with the platform role <c>AiBudgetAdmin</c>; the AI service refuses everybody else.
/// </summary>
internal class SetAiSpendCapCommand : ServiceClientOctoCommand<IAiServicesClient>
{
    public const string CommandName = "SetAiSpendCap";
    internal const int MaxReasonLength = 500;

    internal static readonly string[] KeySources = ["CentralKey", "None", "ByoKey", "Subscription"];

    private readonly IArgument _capArg;
    private readonly IArgument _keySourceArg;
    private readonly IArgument _reasonArg;
    private readonly IArgument _tenantArg;

    public SetAiSpendCapCommand(ILogger<SetAiSpendCapCommand> logger, IOptions<OctoToolOptions> options,
        IAiServicesClient aiServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.AiServicesGroup, "SetAiSpendCap", // literal for the command-reference generator
            "Sets the monthly LLM spend cap in EUR of a tenant for the central-key LLM gateway. Platform role " +
            "AiBudgetAdmin in the system tenant only; every change is audited (old, new, who, why).",
            options, aiServicesClient, authenticationService)
    {
        _tenantArg = CommandArgumentValue.AddArgument("t", "tenantId",
            ["Id of the tenant whose cap is set"], true, 1);
        _capArg = CommandArgumentValue.AddArgument("c", "capMonthlyEur",
            ["Monthly cap in EUR (UTC calendar month), e.g. 20 or 12.5; 0 stops the tenant's central-key calls"], true, 1);
        _reasonArg = CommandArgumentValue.AddArgument("r", "reason",
            ["Why the cap changes (stored in the audit record, max. 500 characters)"], true, 1);
        _keySourceArg = CommandArgumentValue.AddArgument("ks", "keySource",
            ["Key source: CentralKey, None, ByoKey or Subscription. Not given: the tenant keeps its current key source (CentralKey when it has none yet). Only CentralKey is served by the gateway"],
            false, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(
                    arguments:
                    [
                        new CodeSampleArgument(_tenantArg, "ai-sandbox"),
                        new CodeSampleArgument(_capArg, "20"),
                        new CodeSampleArgument(_reasonArg, "pilot budget AB#8690")
                    ],
                    description:
                    "Set a monthly cap of EUR 20 (key source kept; a tenant without an entry gets CentralKey). Add -ks CentralKey to switch a tenant with another key source to the central key")
            ],
            Notes:
            [
                "Run it in a context logged in to the system tenant with the role AiBudgetAdmin and full API access. A " +
                "tenant administrator, or a holder of AiBudgetAdmin in a child tenant, is refused (403).",
                "The cap is stored in the system database, not in the tenant: tenants cannot raise it. AiQuotaLimit in the " +
                "tenant stays a self-limit for a tenant's own key.",
                "The gateway uses the new cap within its cap cache time (at most 60 seconds), without a restart.",
                "Read cap and spend with GetAiSpend."
            ]);

    public override async Task Execute()
    {
        var tenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantArg).Trim();
        var capText = CommandArgumentValue.GetArgumentScalarValue<string>(_capArg).Trim();
        var reason = CommandArgumentValue.GetArgumentScalarValue<string>(_reasonArg).Trim();
        var keySourceGiven = CommandArgumentValue.IsArgumentUsed(_keySourceArg);
        var keySource = keySourceGiven
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_keySourceArg).Trim()
            : "CentralKey";

        if (!decimal.TryParse(capText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var cap) ||
            cap < 0)
        {
            throw new ToolException($"The cap '{capText}' is not a decimal number of 0 or more (use '.' as separator).");
        }

        var knownKeySource = KeySources.FirstOrDefault(k => string.Equals(k, keySource, StringComparison.OrdinalIgnoreCase));
        if (knownKeySource == null)
        {
            throw new ToolException($"The key source '{keySource}' is not one of {string.Join(", ", KeySources)}.");
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Length > MaxReasonLength)
        {
            throw new ToolException($"A reason of 1 to {MaxReasonLength} characters is required.");
        }

        if (!keySourceGiven)
        {
            // Changing only the cap must not switch a ByoKey, Subscription or deliberately blocked (None) tenant to the
            // central key: keep the stored key source; only a tenant without an entry yet (None, no change date) gets
            // CentralKey. A stored value this command does not know (or none) is refused instead of defaulting to
            // CentralKey, which would open the central key on a cap-only change.
            var current = await ServiceClient.GetAiSpendCapAsync(tenantId);
            var stored = KeySources.FirstOrDefault(k =>
                string.Equals(k, current.KeySource, StringComparison.OrdinalIgnoreCase));
            if (stored == null)
            {
                throw new ToolException(
                    $"The stored key source '{current.KeySource}' of tenant '{tenantId}' is not one of " +
                    $"{string.Join(", ", KeySources)}; give the key source explicitly with -ks.");
            }

            knownKeySource = stored == "None" && current.CapChangedAt == null ? "CentralKey" : stored;
        }

        Logger.LogInformation("Setting the LLM spend cap of tenant '{TenantId}' at '{ServiceClientServiceUri}'",
            tenantId, ServiceClient.ServiceUri);

        var change = await ServiceClient.SetAiSpendCapAsync(tenantId,
            new SetAiSpendCapRequestDto { CapMonthlyEur = cap, KeySource = knownKeySource, Reason = reason });

        Logger.LogInformation(
            "LLM spend cap of tenant '{TenantId}' changed by {ChangedBy}: {OldKeySource} {OldCap} EUR -> {NewKeySource} {NewCap} EUR",
            change.TenantId, change.ChangedBy, change.OldKeySource,
            change.OldCapMonthlyEur.ToString(CultureInfo.InvariantCulture), change.NewKeySource,
            change.NewCapMonthlyEur.ToString(CultureInfo.InvariantCulture));
    }
}
