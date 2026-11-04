using System.Globalization;
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
        "Lists every rollup archive attached to the given source CkArchive — runtime id, status, schedule, watermark, freeze state, reference time zone and day start.",
        options, serviceClient, authenticationService)
    {
        _archiveRtIdArg = CommandArgumentValue.AddArgument("id", "identifier",
            ["Runtime id of the source CkArchive entity"], true, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_archiveRtIdArg, "6700000000000000000000b1")],
                    description: "List the rollups of a source archive",
                    expectedOutput:
                    "  6700000000000000000000a1 (gasday) — Activated, bucket=86400000ms, lag=60000ms, watermark=<unset>, " +
                    "frozenUntil=<not frozen>, aggs=1, timeZone=Europe/Vienna, dayStart=06:00")
            ],
            Notes:
            [
                "timeZone is the rollup's reference time zone (IANA id) and dayStart the local time at which a CalendarDay " +
                "bucket starts: a gas-day rollup shows timeZone=Europe/Vienna, dayStart=06:00, so its days are 23, 24 or 25 " +
                "hours long across DST changes. dayStart=00:00 means midnight days; <none> means the rollup has no reference " +
                "time zone (fixed-size buckets in UTC) or the server does not report it.",
                "Rollups are created by blueprints or GraphQL (createRollupArchive), not by octo-cli."
            ]);

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
                "  {RtId} ({Name}) — {Status}, bucket={BucketMs}ms, lag={LagMs}ms, watermark={Watermark}, frozenUntil={Frozen}, aggs={AggCount}, timeZone={TimeZone}, dayStart={DayStart}",
                r.RtId,
                r.RtWellKnownName ?? "<unnamed>",
                r.Status,
                r.BucketSizeMs,
                r.WatermarkLagMs,
                r.LastAggregatedBucketEnd?.ToString("O") ?? "<unset>",
                r.FrozenUntil?.ToString("O") ?? "<not frozen>",
                r.AggregationCount,
                // AB#7362: a gas-day rollup reads timeZone=Europe/Vienna, dayStart=06:00.
                r.ReferenceTimeZone ?? "<none>",
                FormatDayStart(r.DayStartOffsetMs, r.ReferenceTimeZone));
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
            // Multi-source rollups (AB#5157). Printed for every rollup that declares sources, not
            // only for the multi-source or span-limited ones: the line was the only place the
            // listing states what a rollup actually reads from, so suppressing it in the single
            // unbounded case left a rollup whose deprecated scalar disagrees with its Sources
            // showing no source at all (AB#5157 validation finding 4).
            if (r.Sources is { Count: > 0 } sources)
            {
                // Half-open spans [from, to): 'from' inclusive, 'to' exclusive, unbounded ends as -inf/+inf.
                var spans = string.Join(", ", sources.Select(s =>
                    $"{s.SourceArchiveRtId} [{s.ValidFrom?.ToString("O") ?? "-inf"}, {s.ValidTo?.ToString("O") ?? "+inf"})"));
                Logger.LogInformation("      sources: {Sources}", spans);
            }
        }
    }

    /// <summary>
    ///     The day start of a rollup as local <c>HH:mm</c> (AB#7362): <c>06:00</c> for 21600000 ms, <c>00:00</c> for
    ///     midnight days, <c>&lt;none&gt;</c> when the rollup has no reference time zone or the server reports neither.
    ///     The asset repository returns a null day start for a midnight rollup created without one, so a null day start
    ///     with a reference time zone is midnight (<c>00:00</c>).
    /// </summary>
    internal static string FormatDayStart(long? dayStartOffsetMs, string? referenceTimeZone) => dayStartOffsetMs switch
    {
        { } ms => TimeSpan.FromMilliseconds(ms).ToString(@"hh\:mm", CultureInfo.InvariantCulture),
        null when !string.IsNullOrEmpty(referenceTimeZone) => "00:00",
        _ => "<none>"
    };
}
