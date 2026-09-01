using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Models;

internal class ExportRuntimeModelByDeepGraph : JobOctoCommand
{
    private readonly IAssetServicesClient _assetServicesClient;
    private readonly IArgument _fileArg;
    private readonly IArgument _followSpecsArg;
    private readonly IArgument _originCkTypeIdArg;
    private readonly IArgument _originRtIdsArg;

    public ExportRuntimeModelByDeepGraph(ILogger<ExportRuntimeModelByDeepGraph> logger,
        IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IBotServicesClient botServiceClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "ExportRtByDeepGraph",
            "Schedules a job to export runtime model graph by providing RtId's and type as starting point. File is specified using -f argument. The file is downloaded in ZIP-format after job is finished.",
            options, botServiceClient, authenticationService)
    {
        _assetServicesClient = assetServicesClient;

        _fileArg = CommandArgumentValue.AddArgument("f", "file", ["File to export"], true, 1);
        _originRtIdsArg =
            CommandArgumentValue.AddArgument("id", "runtime-identifiers",
                ["A semicolon separated list of RtIds to be used as starting point."], true, 1, true);
        _originCkTypeIdArg =
            CommandArgumentValue.AddArgument("t", "ckTypeId",
                ["The construction kit type id to be used as starting point."], true, 1);
        _followSpecsArg =
            CommandArgumentValue.AddArgument("far", "follow-associations",
                [
                    "Optional semicolon separated list of directed follow rules 'roleId:direction' " +
                    "the traversal applies (direction = Inbound or Outbound), e.g. " +
                    "'System.Identity/PolicyPermission:Inbound;System.Identity/GrantsPermission:Inbound'. " +
                    "Default: hierarchical System/ParentChild traversal."
                ], false, 1, true);
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
                    new CodeSampleArgument(_fileArg, "./export.zip"),
                    new CodeSampleArgument(_originRtIdsArg, "rtId1;rtId2;rtId3"),
                    new CodeSampleArgument(_originCkTypeIdArg, "MyNamespace/MyType-1"),
                ],
                    description: "Basic usage"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_fileArg, "./permission-export.zip"),
                    new CodeSampleArgument(_originRtIdsArg, "rtId1"),
                    new CodeSampleArgument(_originCkTypeIdArg, "System.Identity/DataPermission"),
                    new CodeSampleArgument(_followSpecsArg,
                        "System.Identity/PolicyPermission:Inbound;System.Identity/GrantsPermission:Inbound"),
                ],
                    description: "Export a data permission with its policies and granting roles"),
            ]
        );

    public override async Task Execute()
    {
        var rtModelFilePath = CommandArgumentValue.GetArgumentScalarValue<string>(_fileArg);
        var originRtIdsArgumentValue = CommandArgumentValue.GetArgumentValue(_originRtIdsArg);
        var originCkTypeId = CommandArgumentValue.GetArgumentScalarValue<string>(_originCkTypeIdArg);
        var originRtIds = originRtIdsArgumentValue.Values.Select(OctoObjectId.Parse).ToList();

        var tenantId = Options.Value.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw ToolException.NoTenantIdConfigured();
        }

        if (File.Exists(rtModelFilePath))
        {
            Logger.LogError("File \'{RtModelFilePath}\' already exists", rtModelFilePath);
            return;
        }

        Logger.LogInformation("Exporting runtime data as deep graph to \'{RtModelFilePath}\'", rtModelFilePath);

        List<DeepGraphFollowSpecDto>? followSpecs = null;
        if (CommandArgumentValue.IsArgumentUsed(_followSpecsArg))
        {
            followSpecs = [];
            foreach (var raw in CommandArgumentValue.GetArgumentValue(_followSpecsArg).Values)
            {
                var separatorIndex = raw.LastIndexOf(':');
                if (separatorIndex <= 0 || separatorIndex == raw.Length - 1 ||
                    !Enum.TryParse<GraphDirections>(raw[(separatorIndex + 1)..], true, out var direction))
                {
                    throw new ToolException(
                        $"Invalid follow rule '{raw}'. Expected 'roleId:Inbound' or 'roleId:Outbound'.");
                }

                followSpecs.Add(new DeepGraphFollowSpecDto
                {
                    RoleId = raw[..separatorIndex],
                    Direction = direction
                });
            }
        }

        var id = await _assetServicesClient.ExportRtModelByDeepGraphAsync(tenantId, originRtIds, originCkTypeId,
            followSpecs);
        Logger.LogInformation("Runtime model export with job id \'{Id}\' has been started", id);
        await WaitForJob(id);

        await DownloadJobResultAsync(tenantId, id, rtModelFilePath);
    }
}