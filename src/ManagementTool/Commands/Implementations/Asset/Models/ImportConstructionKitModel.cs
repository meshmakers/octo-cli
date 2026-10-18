using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.CkModelLibraries;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.CkModelCatalog;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Models;

internal class ImportConstructionKitModel : JobWithWaitOctoCommand
{
    private readonly IAssetServicesClient _assetServicesClient;
    private readonly IArgument _fileArg;
    private readonly IArgument _forceBreakingUpdateArg;

    public ImportConstructionKitModel(ILogger<ImportConstructionKitModel> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IBotServicesClient botServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "ImportCk",
            "Schedules an import job for construction kit files. File is specified using -f argument. To wait for job, use -w argument. " +
            "The tenant upgrade guard refuses a file that would break an installed model (also for System) with CK_UPGRADE_BLOCKED; " +
            "--forceBreakingUpdate overrides it.",
            options, botServicesClient, authenticationService)
    {
        _assetServicesClient = assetServicesClient;

        _fileArg = CommandArgumentValue.AddArgument("f", "file", ["File to import"], true, 1);
        // -f is the file, so the override of the tenant upgrade guard has its own name (the same as for the blueprints).
        _forceBreakingUpdateArg = CommandArgumentValue.AddArgument("fbu", "forceBreakingUpdate",
            [
                "Import although the file would break an installed CK model (tenant upgrade guard, also for System).",
                "The broken models become ResolveFailed or the tenant may fail to load. Without it the service refuses with CK_UPGRADE_BLOCKED and nothing is changed. Needs the data model management role."
            ],
            false, 0);
    }

    public override async Task PreValidate()
    {
        await base.PreValidate();

        _assetServicesClient.AccessToken.AccessToken = ServiceClient.AccessToken.AccessToken;
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_fileArg, "./my-ck-model.yaml"),
                    new CodeSampleArgument(_waitForJobArg),
                ],
                    description: "Basic usage"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_fileArg, "./my-ck-model.yaml"),
                    new CodeSampleArgument(_forceBreakingUpdateArg),
                ],
                    description: "Import although the file breaks an installed model (deliberate override of the tenant upgrade guard)"),
            ]
        );

    public override async Task Execute()
    {
        var ckModelFilePath = CommandArgumentValue.GetArgumentScalarValue<string>(_fileArg);

        var tenantId = Options.Value.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw ToolException.NoTenantIdConfigured();
        }

        if (!File.Exists(ckModelFilePath))
        {
            // AB#2615: a missing input file is an error (non-zero exit), not a silent return.
            throw ToolException.FilePathDoesNotExist(ckModelFilePath);
        }

        // AB#2615: reject an empty CK before anything is sent to the server.
        CkModelFileInspector.EnsureNotEmpty(ckModelFilePath);

        Logger.LogInformation("Importing construction kit model \'{CkModelFilePath}\'", ckModelFilePath);

        var force = CommandArgumentValue.IsArgumentUsed(_forceBreakingUpdateArg);
        if (force)
        {
            Logger.LogWarning(
                "--forceBreakingUpdate: importing even if the file breaks installed models (they become ResolveFailed or the tenant may fail to load)");
        }

        string id;
        try
        {
            id = await _assetServicesClient.ImportCkModelAsync(tenantId, ckModelFilePath, force);
        }
        catch (CkUpgradeBlockedException blocked)
        {
            Logger.LogError("The file would break installed models; nothing was imported:");
            UpgradeGuardReport.LogBlockers(Logger, LogLevel.Error, blocked.Blockers);
            throw new ToolException(
                $"The import of {string.Join(", ", blocked.UpdateSet)} would break " +
                $"{blocked.Blockers.Select(b => b.Dependent).Distinct().Count()} installed model(s) (CK_UPGRADE_BLOCKED). " +
                "Update the dependent models first, or repeat the command with --forceBreakingUpdate to import anyway.",
                blocked);
        }

        Logger.LogInformation("Construction kit model import with job id \'{Id}\' has been started", id);
        await WaitForJob(id);
    }
}