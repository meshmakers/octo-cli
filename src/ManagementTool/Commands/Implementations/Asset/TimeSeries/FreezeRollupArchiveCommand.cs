using System.Globalization;
using GraphQlDtos.DataTransferObjects.System.StreamData.v1;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

public class FreezeRollupArchiveCommand : ServiceClientOctoCommand<IStreamDataServicesClient>
{
    private readonly ArchiveMethodCall _methods;
    private readonly IArgument _rollupRtIdArg;
    private readonly IArgument _untilArg;

    public FreezeRollupArchiveCommand(ILogger<FreezeRollupArchiveCommand> logger,
        IOptions<OctoToolOptions> options, IStreamDataServicesClient serviceClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService) : base(logger, Constants.AssetRepositoryServicesGroup,
        "FreezeRollupArchive",
        "Freezes a CkRollupArchive at the given timestamp. Monotonic — rejected when the new value is earlier than the current FrozenUntil. The orchestrator stops producing buckets whose bucketEnd falls within the frozen range; already-aggregated rows are preserved.",
        options, serviceClient, authenticationService)
    {
        _methods = new ArchiveMethodCall(tenantClient, authenticationService, logger);
        _rollupRtIdArg = CommandArgumentValue.AddArgument("id", "identifier",
            ["Runtime id of the CkRollupArchive entity to freeze"], true, 1);
        _untilArg = CommandArgumentValue.AddArgument("u", "until",
            ["Inclusive upper bound of the frozen range, ISO-8601 (e.g. 2026-05-11T14:00:00Z)"], true, 1);
    }

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        var rollupRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_rollupRtIdArg);
        var untilRaw = CommandArgumentValue.GetArgumentScalarValue<string>(_untilArg);

        if (!DateTime.TryParse(untilRaw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var until))
        {
            Logger.LogError("Could not parse 'until' value '{Raw}' as ISO-8601 timestamp.", untilRaw);
            return;
        }

        Logger.LogInformation(
            "Freezing rollup archive '{RollupRtId}' for tenant '{TenantId}' until {Until:O}",
            rollupRtId, Options.Value.TenantId, until);

        ArchiveMethodCall.RequireObjectId(rollupRtId, "RollupRtId");
        await _methods.RunAsync(RtRollupArchiveMethodClient.FreezeMethodId,
            () => new RtRollupArchiveMethodClient(_methods.TenantClient).FreezeAsync(rollupRtId, until));

        Logger.LogInformation(
            "Rollup archive '{RollupRtId}' frozen until {Until:O}", rollupRtId, until);
    }
}
