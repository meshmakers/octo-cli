using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Blueprints;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Blueprints;

internal class UpdateBlueprint : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IArgument _targetVersionArg;
    private readonly IArgument _updateModeArg;
    private readonly IArgument _dryRunArg;
    private readonly IArgument _allowBlankingArg;
    private readonly IArgument _confirmBlankingArg;

    public UpdateBlueprint(
        ILogger<UpdateBlueprint> logger,
        IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "UpdateBlueprint",
            "Applies a blueprint update to the active tenant. Tenant values the update would blank are kept unless confirmed.",
            options, assetServicesClient, authenticationService)
    {
        _targetVersionArg = CommandArgumentValue.AddArgument("tv", "targetVersion",
            ["Fully-qualified target blueprint id, e.g. 'MyBlueprint-2.0.0'"], true, 1);

        _updateModeArg = CommandArgumentValue.AddArgument("m", "updateMode",
            ["Update mode: Safe, Merge (default), Full, or Migration"], false, 1);

        _dryRunArg = CommandArgumentValue.AddArgument("dr", "dry-run",
            ["Simulate the update without persisting changes"], false, 0);

        _allowBlankingArg = CommandArgumentValue.AddArgument("ab", "allow-blanking",
            [
                "Confirm that the update may blank EVERY tenant value listed by PreviewBlueprintUpdate.",
                "Use --confirm-blanking to confirm single attributes instead."
            ], false, 0);

        _confirmBlankingArg = CommandArgumentValue.AddArgument("cb", "confirm-blanking",
            [
                "Confirm blanking of one attribute: '<rtId>:<attribute>' as listed by PreviewBlueprintUpdate.",
                "Repeat the option for several attributes; everything not confirmed is kept."
            ], false, 1, true);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(
                    arguments: [new CodeSampleArgument(_targetVersionArg, "Eda.Adapter-1.2.0")],
                    description: "Update safely: tenant values the seed would blank are kept and listed"),
                new CodeSample(
                    arguments:
                    [
                        new CodeSampleArgument(_targetVersionArg, "Eda.Adapter-1.2.0"),
                        new CodeSampleArgument(_confirmBlankingArg, "65a1b2c3d4e5f60718293a4b:configuration"),
                        new CodeSampleArgument(_confirmBlankingArg, "65a1b2c3d4e5f60718293a4c:password")
                    ],
                    description: "Blank exactly the confirmed attributes (rtId and attribute from PreviewBlueprintUpdate)"),
                new CodeSample(
                    arguments:
                    [
                        new CodeSampleArgument(_targetVersionArg, "Eda.Adapter-1.2.0"),
                        new CodeSampleArgument(_allowBlankingArg)
                    ],
                    description: "Blank every listed attribute (loud warning; prefer --confirm-blanking)"),
            ],
            Notes:
            [
                "A blueprint seed that carries an empty value, or omits an attribute, would clear what the tenant has entered.",
                "Without --confirm-blanking or --allow-blanking the update still runs but keeps those tenant values; the command lists them and exits with code 0.",
                "Run PreviewBlueprintUpdate first: it lists entity, attribute, reason and a kind/size summary of the current and incoming value (never the value).",
                "--allow-blanking confirms every listed attribute and takes precedence over --confirm-blanking.",
                "A --confirm-blanking pair that is not a blanking candidate is reported and ignored.",
                "Against services older than the blanking protection (AB#6315) the update behaves as before and no blanking list is reported.",
            ]);

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing - configure it via the active context");
            return;
        }

        var targetVersion = CommandArgumentValue.GetArgumentScalarValue<string>(_targetVersionArg);
        var updateMode = CommandArgumentValue.IsArgumentUsed(_updateModeArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_updateModeArg)
            : "Merge";
        var dryRun = CommandArgumentValue.IsArgumentUsed(_dryRunArg);
        var allowBlanking = CommandArgumentValue.IsArgumentUsed(_allowBlankingArg);
        var confirmations = ParseConfirmations();

        if (allowBlanking)
        {
            Logger.LogWarning(
                "--allow-blanking: EVERY tenant value the update would blank is cleared. " +
                "Use --confirm-blanking <rtId>:<attribute> to confirm single attributes.");
            if (confirmations.Count > 0)
            {
                Logger.LogWarning("--confirm-blanking is ignored because --allow-blanking confirms everything.");
            }
        }

        Logger.LogInformation(
            "Applying update of tenant '{TenantId}' to '{TargetVersion}' (mode={UpdateMode}, dryRun={DryRun}, " +
            "allowBlanking={AllowBlanking}, confirmedBlankings={ConfirmedBlankings})",
            Options.Value.TenantId, targetVersion, updateMode, dryRun, allowBlanking, confirmations.Count);

        var request = new BlueprintUpdateRequestDto
        {
            TargetVersion = targetVersion,
            UpdateMode = updateMode,
            DryRun = dryRun,
            AllowBlanking = allowBlanking,
            ConfirmedBlankings = allowBlanking || confirmations.Count == 0 ? null : confirmations
        };

        var result = await ServiceClient.ApplyBlueprintUpdateAsync(Options.Value.TenantId, request);

        Logger.LogInformation(dryRun
                ? "Blueprint update of tenant '{TenantId}' simulated, nothing was changed"
                : "Blueprint update applied to tenant '{TenantId}'",
            Options.Value.TenantId);

        ReportBlanking(result.BlankedAttributes, dryRun, allowBlanking ? [] : confirmations);
    }

    private List<BlueprintBlankingConfirmationDto> ParseConfirmations()
    {
        if (!CommandArgumentValue.IsArgumentUsed(_confirmBlankingArg))
        {
            return [];
        }

        return CommandArgumentValue.GetArgumentValue(_confirmBlankingArg).Values
            .Select(BlueprintBlanking.ParseConfirmation)
            .DistinctBy(c => (c.RtId.ToLowerInvariant(), c.AttributeName.ToLowerInvariant()))
            .ToList();
    }

    private void ReportBlanking(
        IReadOnlyCollection<BlueprintBlankedAttributeDto> blanked,
        bool dryRun,
        IReadOnlyCollection<BlueprintBlankingConfirmationDto> confirmations)
    {
        var kept = blanked.Where(b => !b.AppliedOnUpdate).ToList();
        var applied = blanked.Where(b => b.AppliedOnUpdate).ToList();

        if (applied.Count > 0)
        {
            Logger.LogWarning("{Count} tenant value(s) were BLANKED on your confirmation:", applied.Count);
            foreach (var line in BlueprintBlanking.FormatTable(applied))
            {
                Logger.LogWarning("{Line}", line);
            }
        }

        if (kept.Count > 0)
        {
            Logger.LogWarning(
                dryRun
                    ? "{Count} tenant value(s) would be KEPT; the seed would blank them:"
                    : "{Count} tenant value(s) were KEPT; the seed would have blanked them:",
                kept.Count);
            foreach (var line in BlueprintBlanking.FormatTable(kept))
            {
                Logger.LogWarning("{Line}", line);
            }

            Logger.LogWarning(
                "To blank an attribute, confirm it explicitly, e.g. '{Example}', or use '--allow-blanking' for all.",
                BlueprintBlanking.ConfirmationArgument(kept[0]));
        }

        foreach (var unmatched in confirmations.Where(c => !blanked.Any(b => BlueprintBlanking.SameAttribute(c, b))))
        {
            Logger.LogWarning(
                "--confirm-blanking '{RtId}:{AttributeName}' is not a blanking candidate of this update and was ignored " +
                "(the service may also predate the blanking protection).",
                unmatched.RtId, unmatched.AttributeName);
        }

        if (blanked.Count == 0 && confirmations.Count == 0)
        {
            Logger.LogInformation("The service reported no tenant values that the update would blank.");
        }
        else if (dryRun && blanked.Count > 0)
        {
            Logger.LogInformation("Dry run: the lists above show what a real update would do.");
        }
    }
}
