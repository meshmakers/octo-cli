using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;

/// <summary>
///     AB#5945: repairs the engine-computed display fields (rtDisplayName / rtDisplayDescription) of a
///     tenant by enqueueing idempotent display-rule sweep tasks on the asset repository.
/// </summary>
internal class RecomputeDisplayNames : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _tenantIdArg;
    private readonly IArgument _ckTypeIdArg;

    public RecomputeDisplayNames(ILogger<RecomputeDisplayNames> logger,
        IConsoleService consoleService,
        IOptions<OctoToolOptions> options, IAssetServicesClient assetServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "RecomputeDisplayNames",
            "Recomputes rtDisplayName/rtDisplayDescription of a tenant's entities from the CK display rules (idempotent, runs asynchronously).",
            options, assetServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId", ["Id of tenant"], true, 1);
        _ckTypeIdArg = CommandArgumentValue.AddArgument("ck", "ckTypeId",
            ["Optional CK type id (e.g. Meshmakers.Accounting/FiscalYear); all types with a display rule when omitted"],
            false, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_tenantIdArg, "meshmakers")],
                    description: "Recompute all display names of a tenant"),
                new CodeSample(
                    arguments:
                    [
                        new CodeSampleArgument(_tenantIdArg, "meshmakers"),
                        new CodeSampleArgument(_ckTypeIdArg, "Meshmakers.Accounting/FiscalYear")
                    ],
                    description: "Recompute one CK type (and its subtypes)")
            ]
        );

    public override async Task Execute()
    {
        var tenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg).ToLower();
        var ckTypeId = CommandArgumentValue.IsArgumentUsed(_ckTypeIdArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_ckTypeIdArg)
            : null;

        var sweepKeys = await ServiceClient.RecomputeTenantDisplayNamesAsync(tenantId, ckTypeId);

        Logger.LogInformation(
            "Display name recompute for tenant '{TenantId}' enqueued for {Count} CK type(s); the asset repository sweeps them asynchronously.",
            tenantId, sweepKeys.Count);
        foreach (var sweepKey in sweepKeys)
        {
            _consoleService.WriteLine(sweepKey);
        }
    }
}
