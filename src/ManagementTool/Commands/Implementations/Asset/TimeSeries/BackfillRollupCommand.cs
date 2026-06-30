using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

public class BackfillRollupCommand : ServiceClientOctoCommand<IStreamDataServicesClient>
{
    private readonly IArgument _rollupRtIdArg;

    public BackfillRollupCommand(ILogger<BackfillRollupCommand> logger,
        IOptions<OctoToolOptions> options, IStreamDataServicesClient serviceClient,
        IAuthenticationService authenticationService) : base(logger, Constants.AssetRepositoryServicesGroup,
        "BackfillRollup",
        "Populates / resets a rollup over the ENTIRE history of its source archive without supplying a timestamp (AB#4269). Resolves the source archive's earliest timestamp and recomputes [sourceMin, now) over the reader-safe optimistic recompute path. A no-op when the source archive holds no data.",
        options, serviceClient, authenticationService)
    {
        _rollupRtIdArg = CommandArgumentValue.AddArgument("id", "identifier",
            ["Runtime id of the CkRollupArchive to backfill from its source"], true, 1);
    }

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        var rollupRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_rollupRtIdArg);

        Logger.LogInformation(
            "Triggering backfill-from-source for rollup '{RollupRtId}' (tenant '{TenantId}')",
            rollupRtId, Options.Value.TenantId);

        var job = await ServiceClient.BackfillRollupFromSourceAsync(Options.Value.TenantId, rollupRtId);

        if (job is null)
        {
            Logger.LogInformation(
                "Backfill of rollup '{RollupRtId}' was a no-op: the source archive holds no data.",
                rollupRtId);
            return;
        }

        Logger.LogInformation(
            "Backfill job {RtId}: state={State}, rows={Rows}, windows={Windows}, duration={Duration}ms, error={Error}",
            job.RtId, job.State,
            job.RowsProcessed?.ToString() ?? "n/a",
            job.WindowsProcessed?.ToString() ?? "n/a",
            job.DurationMs?.ToString() ?? "n/a",
            job.ErrorReason ?? "none");
    }
}
