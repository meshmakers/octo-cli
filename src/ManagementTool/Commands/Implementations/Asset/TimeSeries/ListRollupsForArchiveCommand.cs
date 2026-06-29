using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

public class ListRollupsForArchiveCommand : ServiceClientOctoCommand<IStreamDataServicesClient>
{
    private readonly IArgument _archiveRtIdArg;

    public ListRollupsForArchiveCommand(ILogger<ListRollupsForArchiveCommand> logger,
        IOptions<OctoToolOptions> options, IStreamDataServicesClient serviceClient,
        IAuthenticationService authenticationService) : base(logger, Constants.AssetRepositoryServicesGroup,
        "ListRollupsForArchive",
        "Lists every rollup archive attached to the given source CkArchive — runtime id, status, schedule, watermark, freeze state.",
        options, serviceClient, authenticationService)
    {
        _archiveRtIdArg = CommandArgumentValue.AddArgument("id", "identifier",
            ["Runtime id of the source CkArchive entity"], true, 1);
    }

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        var archiveRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_archiveRtIdArg);

        Logger.LogInformation(
            "Listing rollups for archive '{ArchiveRtId}' (tenant '{TenantId}')",
            archiveRtId, Options.Value.TenantId);

        var rollups = await ServiceClient.ListRollupsForArchiveAsync(Options.Value.TenantId, archiveRtId);

        if (rollups.Count == 0)
        {
            Logger.LogInformation("No rollups attached to archive '{ArchiveRtId}'.", archiveRtId);
            return;
        }

        Logger.LogInformation("{Count} rollup(s) attached:", rollups.Count);
        foreach (var r in rollups)
        {
            Logger.LogInformation(
                "  {RtId} ({Name}) — {Status}, bucket={BucketMs}ms, lag={LagMs}ms, watermark={Watermark}, frozenUntil={Frozen}, aggs={AggCount}",
                r.RtId,
                r.RtWellKnownName ?? "<unnamed>",
                r.Status,
                r.BucketSizeMs,
                r.WatermarkLagMs,
                r.LastAggregatedBucketEnd?.ToString("O") ?? "<unset>",
                r.FrozenUntil?.ToString("O") ?? "<not frozen>",
                r.AggregationCount);
            // Recompute health (AB#4184) — surfaced as a second line so the steady-state view stays
            // compact while failures / pending work are still visible for debugging.
            Logger.LogInformation(
                "      recompute: inProgress={InProgress}, dirtyWindows={Dirty}, pendingRanges={Pending}, lastSuccess={Success}, lastFailure={Failure}{Reason}",
                r.RecomputeInProgress,
                r.DirtyWindowsPending,
                r.PendingRecomputeRanges,
                r.LastRecomputeSuccessAt?.ToString("O") ?? "<never>",
                r.LastRecomputeFailureAt?.ToString("O") ?? "<none>",
                string.IsNullOrEmpty(r.LastRecomputeFailureReason) ? "" : $" ({r.LastRecomputeFailureReason})");
        }
    }
}
