using System.Globalization;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

public class RecomputeArchiveCommand : ServiceClientOctoCommand<IStreamDataServicesClient>
{
    private readonly IArgument _rollupRtIdArg;
    private readonly IArgument _fromArg;
    private readonly IArgument _toArg;
    private readonly IArgument _rtIdScopeArg;

    public RecomputeArchiveCommand(ILogger<RecomputeArchiveCommand> logger,
        IOptions<OctoToolOptions> options, IStreamDataServicesClient serviceClient,
        IAuthenticationService authenticationService) : base(logger, Constants.AssetRepositoryServicesGroup,
        "RecomputeArchive",
        "Triggers (or coalesces) an optimistic recompute of a rollup archive over [from, to). Returns the resulting job snapshot (state, counts, error reason).",
        options, serviceClient, authenticationService)
    {
        _rollupRtIdArg = CommandArgumentValue.AddArgument("id", "identifier",
            ["Runtime id of the CkRollupArchive to recompute"], true, 1);
        _fromArg = CommandArgumentValue.AddArgument("f", "from",
            ["Inclusive range start, ISO-8601"], true, 1);
        _toArg = CommandArgumentValue.AddArgument("t", "to",
            ["Exclusive range end, ISO-8601"], true, 1);
        _rtIdScopeArg = CommandArgumentValue.AddArgument("s", "rtIdScope",
            ["Optional: recompute only this entity's data (metering point / stream)"], false, 1);
    }

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            Logger.LogError("TenantId is missing");
            return;
        }

        var rollupRtId = CommandArgumentValue.GetArgumentScalarValue<string>(_rollupRtIdArg);
        var fromRaw = CommandArgumentValue.GetArgumentScalarValue<string>(_fromArg);
        var toRaw = CommandArgumentValue.GetArgumentScalarValue<string>(_toArg);
        var rtIdScope = CommandArgumentValue.IsArgumentUsed(_rtIdScopeArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_rtIdScopeArg)
            : null;

        if (!DateTime.TryParse(fromRaw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var from))
        {
            Logger.LogError("Could not parse 'from' value '{Raw}' as ISO-8601 timestamp.", fromRaw);
            return;
        }

        if (!DateTime.TryParse(toRaw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var to))
        {
            Logger.LogError("Could not parse 'to' value '{Raw}' as ISO-8601 timestamp.", toRaw);
            return;
        }

        Logger.LogInformation(
            "Triggering recompute for rollup '{RollupRtId}' (tenant '{TenantId}') over [{From:O}, {To:O})",
            rollupRtId, Options.Value.TenantId, from, to);

        var job = await ServiceClient.RecomputeArchiveAsync(Options.Value.TenantId, rollupRtId, from, to, rtIdScope);

        Logger.LogInformation(
            "Recompute job {RtId}: state={State}, rows={Rows}, windows={Windows}, duration={Duration}ms, error={Error}",
            job.RtId, job.State,
            job.RowsProcessed?.ToString() ?? "n/a",
            job.WindowsProcessed?.ToString() ?? "n/a",
            job.DurationMs?.ToString() ?? "n/a",
            job.ErrorReason ?? "none");
    }
}
