using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

public class ListRecomputeJobsCommand : ServiceClientOctoCommand<IStreamDataServicesClient>
{
    private readonly IArgument _archiveRtIdArg;

    public ListRecomputeJobsCommand(ILogger<ListRecomputeJobsCommand> logger,
        IOptions<OctoToolOptions> options, IStreamDataServicesClient serviceClient,
        IAuthenticationService authenticationService) : base(logger, Constants.AssetRepositoryServicesGroup,
        "ListRecomputeJobs",
        "Lists the most recent recompute jobs for a rollup archive (newest first, capped at 50) — for debugging why a recompute failed.",
        options, serviceClient, authenticationService)
    {
        _archiveRtIdArg = CommandArgumentValue.AddArgument("id", "identifier",
            ["Runtime id of the CkRollupArchive to list recompute jobs for"], true, 1);
    }

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        var archiveRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_archiveRtIdArg);

        Logger.LogInformation("Listing recompute jobs for archive '{ArchiveRtId}' (tenant '{TenantId}')",
            archiveRtId, Options.Value.TenantId);

        var jobs = await ServiceClient.ListRecomputeJobsForArchiveAsync(Options.Value.TenantId, archiveRtId);

        if (jobs.Count == 0)
        {
            Logger.LogInformation("No recompute jobs found for archive '{ArchiveRtId}'.", archiveRtId);
            return;
        }

        Logger.LogInformation("{Count} recompute job(s):", jobs.Count);
        foreach (var j in jobs)
        {
            Logger.LogInformation(
                "  {RtId} — {State}, rows={Rows}, windows={Windows}, started={Started}, finished={Finished}, duration={Duration}ms, error={Error}",
                j.RtId,
                j.State,
                j.RowsProcessed?.ToString() ?? "pending",
                j.WindowsProcessed?.ToString() ?? "pending",
                j.StartedAt?.ToString("O") ?? "<not started>",
                j.FinishedAt?.ToString("O") ?? "<running>",
                j.DurationMs?.ToString() ?? "n/a",
                j.ErrorReason ?? "<none>");
        }
    }
}
