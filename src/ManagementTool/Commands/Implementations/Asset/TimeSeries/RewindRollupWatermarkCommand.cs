using System.Globalization;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

public class RewindRollupWatermarkCommand : ServiceClientOctoCommand<IStreamDataServicesClient>
{
    private readonly IArgument _rollupRtIdArg;
    private readonly IArgument _toBucketEndArg;

    public RewindRollupWatermarkCommand(ILogger<RewindRollupWatermarkCommand> logger,
        IOptions<OctoToolOptions> options, IStreamDataServicesClient serviceClient,
        IAuthenticationService authenticationService) : base(logger, Constants.AssetRepositoryServicesGroup,
        "RewindRollupWatermark",
        "Resets the rollup's watermark (truncated down to the bucket boundary) so subsequent orchestrator ticks re-aggregate the rewound range. Destructive: rows in that range are temporarily out of sync until the orchestrator catches up.",
        options, serviceClient, authenticationService)
    {
        _rollupRtIdArg = CommandArgumentValue.AddArgument("id", "identifier",
            ["Runtime id of the CkRollupArchive entity to rewind"], true, 1);
        _toBucketEndArg = CommandArgumentValue.AddArgument("t", "toBucketEnd",
            ["Target bucket-end (exclusive), ISO-8601. Will be truncated down to the bucket boundary."], true, 1);
    }

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        var rollupRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_rollupRtIdArg);
        var toRaw = CommandArgumentValue.GetArgumentScalarValue<string>(_toBucketEndArg);

        if (!DateTime.TryParse(toRaw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var toBucketEnd))
        {
            Logger.LogError("Could not parse 'toBucketEnd' value '{Raw}' as ISO-8601 timestamp.", toRaw);
            return;
        }

        Logger.LogInformation(
            "Rewinding rollup '{RollupRtId}' watermark for tenant '{TenantId}' to {ToBucketEnd:O}",
            rollupRtId, Options.Value.TenantId, toBucketEnd);

        await ServiceClient.RewindRollupWatermarkAsync(Options.Value.TenantId, rollupRtId, toBucketEnd);

        Logger.LogInformation(
            "Rollup '{RollupRtId}' watermark rewound. Re-aggregation will occur on the next orchestrator tick.",
            rollupRtId);
    }
}
