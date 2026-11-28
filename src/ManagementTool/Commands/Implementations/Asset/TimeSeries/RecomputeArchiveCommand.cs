using System.Globalization;
using GraphQlDtos.DataTransferObjects.System.StreamData.v1;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.Methods;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

public class RecomputeArchiveCommand : ServiceClientOctoCommand<IStreamDataServicesClient>
{
    private readonly ArchiveMethodCall _methods;
    private readonly IArgument _rollupRtIdArg;
    private readonly IArgument _fromArg;
    private readonly IArgument _toArg;
    private readonly IArgument _rtIdScopeArg;

    public RecomputeArchiveCommand(ILogger<RecomputeArchiveCommand> logger,
        IOptions<OctoToolOptions> options, IStreamDataServicesClient serviceClient, ITenantClient tenantClient,
        IAuthenticationService authenticationService) : base(logger, Constants.AssetRepositoryServicesGroup,
        "RecomputeArchive",
        "Triggers (or coalesces) an optimistic recompute of a rollup archive over [from, to). Returns the resulting job snapshot (state, counts, error reason).",
        options, serviceClient, authenticationService)
    {
        _methods = new ArchiveMethodCall(tenantClient, authenticationService, logger);
        _rollupRtIdArg = CommandArgumentValue.AddArgument("id", "identifier",
            ["Runtime id of the CkRollupArchive to recompute"], true, 1);
        _fromArg = CommandArgumentValue.AddArgument("f", "from",
            ["Inclusive range start, ISO-8601"], true, 1);
        _toArg = CommandArgumentValue.AddArgument("t", "to",
            ["Exclusive range end, ISO-8601"], true, 1);
        _rtIdScopeArg = CommandArgumentValue.AddArgument("s", "rtIdScope",
            ["Optional: recompute only this entity's data (metering point / stream)"], false, 1);
    }

    /// <summary>How long the command waits for the recompute to end (as the blocking REST call did).</summary>
    internal TimeSpan WaitTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>The delay between two reads of the operation; <c>null</c> takes the service's suggestion.</summary>
    internal TimeSpan? PollInterval { get; set; }

    /// <summary>The clock of the wait (tests).</summary>
    internal TimeProvider TimeProvider { get; set; } = TimeProvider.System;

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

        var job = await RecomputeAsync(Options.Value.TenantId, rollupRtId, from, to, rtIdScope);

        Logger.LogInformation(
            "Recompute job {RtId}: state={State}, rows={Rows}, windows={Windows}, duration={Duration}ms, " +
            "lastProgress={LastProgress}, error={Error}",
            job.RtId, job.State,
            job.RowsProcessed?.ToString() ?? "n/a",
            job.WindowsProcessed?.ToString() ?? "n/a",
            job.DurationMs?.ToString() ?? "n/a",
            job.LastProgressAt?.ToString("O") ?? "n/a",
            job.ErrorReason ?? "none");
    }

    /// <summary>
    ///     Starts the recompute with <c>RollupArchive.Recompute-2</c> (an operation handle at once) and waits for the
    ///     operation, so the command ends with the completed job, as the blocking REST call did. A job that ended Failed
    ///     (<c>STREAMDATA_RECOMPUTE_FAILED</c>, also a refused precondition such as a rollup that is not activated) is read
    ///     from the archive's recompute jobs and printed with state Failed; the command still exits with 0, as with REST.
    /// </summary>
    private async Task<RollupRecomputeJobInfoDto> RecomputeAsync(string tenantId, string rollupRtId, DateTime from,
        DateTime to, string? rtIdScope)
    {
        ArchiveMethodCall.RequireObjectId(rollupRtId, "RollupRtId");
        var handle = await _methods.RunAsync(RtRollupArchiveMethodClient.Recompute2MethodId,
                () => new RtRollupArchiveMethodClient(_methods.TenantClient).Recompute2Async(rollupRtId, from, to, rtIdScope))
            ?? throw new ServiceClientException(
                $"The CK method {RtRollupArchiveMethodClient.Recompute2MethodId} returned no operation handle.");
        Logger.LogDebug("Recompute of rollup '{RollupRtId}' accepted as operation {OperationId}", rollupRtId,
            handle.OperationId);

        CkMethodResult<RtRecomputeJobInfoRecordDto?> result;
        try
        {
            result = await _methods.WaitForJobAsync(handle, WaitTimeout, TimeProvider, PollInterval);
        }
        catch (CkOperationWaitTimeoutException e)
        {
            throw new ServiceClientException(
                $"The recompute of rollup '{rollupRtId}' did not end within {(int)WaitTimeout.TotalMinutes} min; it keeps " +
                $"running in the background (operation {e.OperationId}). Check it with 'ListRecomputeJobs -id {rollupRtId}'.");
        }

        if (result.IsSuccess)
        {
            return result.Value is null
                ? throw new ServiceClientException(
                    $"The CK method {RtRollupArchiveMethodClient.Recompute2MethodId} returned no recompute job.")
                : ArchiveMethodCall.ToJobInfo(result.Value);
        }

        return await ArchiveMethodCall.FailedJobAsync(ServiceClient, tenantId, rollupRtId, result.Error!);
    }
}
