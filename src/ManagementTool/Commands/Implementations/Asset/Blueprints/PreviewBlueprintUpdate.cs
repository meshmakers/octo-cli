using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Blueprints;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Blueprints;

internal class PreviewBlueprintUpdate : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _targetVersionArg;
    private readonly IArgument _updateModeArg;
    private readonly IArgument _failOnBlankingArg;

    public PreviewBlueprintUpdate(
        ILogger<PreviewBlueprintUpdate> logger,
        IConsoleService consoleService,
        IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "PreviewBlueprintUpdate",
            "Previews the changes a blueprint update would make without applying them.",
            options, assetServicesClient, authenticationService)
    {
        _consoleService = consoleService;

        _targetVersionArg = CommandArgumentValue.AddArgument("tv", "targetVersion",
            ["Fully-qualified target blueprint id, e.g. 'MyBlueprint-2.0.0'"], true, 1);

        _updateModeArg = CommandArgumentValue.AddArgument("m", "updateMode",
            ["Update mode: Safe, Merge (default), Full, or Migration"], false, 1);

        _failOnBlankingArg = CommandArgumentValue.AddArgument("fb", "failOnBlanking",
            ["Exit with a non-zero code (-5) when the update would blank at least one tenant value (for CI gates)"],
            false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(
                    arguments: [new CodeSampleArgument(_targetVersionArg, "Eda.Adapter-1.2.0")],
                    description: "Preview an update; blanking candidates are listed after the JSON result"),
                new CodeSample(
                    arguments:
                    [
                        new CodeSampleArgument(_targetVersionArg, "Eda.Adapter-1.2.0"),
                        new CodeSampleArgument(_failOnBlankingArg)
                    ],
                    description: "CI gate: fail the job when the update would blank a tenant value"),
            ],
            Notes:
            [
                "A blueprint seed that carries an empty value, or omits an attribute, would clear what the tenant has entered.",
                "UpdateBlueprint keeps such tenant values unless they are confirmed with --confirm-blanking or --allow-blanking.",
                "The blanking list shows entity, attribute, reason and a kind/size summary of the current and incoming value - never the value itself.",
                "The Reason column is printed as the service reports it: SeedEmpty, SeedOmitted, or ResetToDefault (the current value differs from the CK default and the incoming value is the default).",
                "Tenant-owned seed entities (rtBlueprintLocked: false) are never rewritten: the output lists those left untouched ('tenant-owned, skipped') and those the tenant deleted that stay deleted, with key, type and rtId ('-' when deleted) and no values. They are not blanking and do not trigger --failOnBlanking.",
                "The JSON result is written to standard output; the blanking table and warnings are log output.",
                "Services older than the blanking protection (AB#6315) report no blanking list.",
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

        Logger.LogInformation(
            "Previewing update of tenant '{TenantId}' to '{TargetVersion}' (mode={UpdateMode})",
            Options.Value.TenantId, targetVersion, updateMode);

        var request = new BlueprintUpdateRequestDto
        {
            TargetVersion = targetVersion,
            UpdateMode = updateMode,
            DryRun = true
        };

        var preview = await ServiceClient.PreviewBlueprintUpdateAsync(Options.Value.TenantId, request);

        var resultString = JsonConvert.SerializeObject(preview, Formatting.Indented);
        _consoleService.WriteLine(resultString);

        BlueprintBlanking.ReportTenantOwned(
            Logger, preview: true, preview.TenantOwnedSkipped, preview.TenantOwnedStaysDeleted);

        var blanked = preview.BlankedAttributes;
        if (blanked.Count == 0)
        {
            return;
        }

        Logger.LogWarning(
            "The update would blank {Count} tenant value(s). UpdateBlueprint keeps them unless you confirm " +
            "(values are never shown, only kind and size):", blanked.Count);
        foreach (var line in BlueprintBlanking.FormatTable(blanked))
        {
            Logger.LogWarning("{Line}", line);
        }

        Logger.LogWarning(
            "Confirm single attributes with '{Example}' or all of them with '--allow-blanking'.",
            BlueprintBlanking.ConfirmationArgument(blanked[0]));

        if (CommandArgumentValue.IsArgumentUsed(_failOnBlankingArg))
        {
            throw ToolException.BlankingDetected(blanked.Count);
        }
    }
}
