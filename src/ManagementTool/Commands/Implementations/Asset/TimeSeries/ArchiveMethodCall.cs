using GraphQL;
using GraphQlDtos;
using GraphQlDtos.DataTransferObjects.System.StreamData.v1;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.Methods;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;

/// <summary>
///     Runs one archive command through a CK method client of <c>System.StreamData</c> (stream-data archives: lifecycle,
///     rollup freeze / unfreeze / rewind, recompute, backfill, archive and computed columns), generated in GraphQlDtos from
///     <c>System.StreamData-[1.30,2.0)</c>. The method call goes to the asset repository of the context's tenant, which runs
///     the handler.
///     <list type="bullet">
///         <item>
///             The tenant client is authenticated first: it carries its own access token, the command's
///             <c>PreValidate</c> authenticated only the stream data client.
///         </item>
///         <item>
///             There is <b>no REST fallback</b>: these commands need an asset repository whose tenant has
///             <c>System.StreamData</c> <see cref="RequiredSystemStreamDataVersion" /> or newer. A server without the method
///             (the document fails GraphQL validation, or <c>METHOD_HANDLER_NOT_AVAILABLE</c>) ends the command with a
///             <see cref="ServiceClientException" /> that names the needed version (exit -3). Nothing was changed then.
///         </item>
///         <item>
///             A declared error of the method (e.g. <c>STREAMDATA_INVALID_TRANSITION</c>) ends the command with a
///             <see cref="ServiceClientException" /> <c>CODE: message (parameters)</c>; the Runner logs it and the command
///             exits with -3, as the refused REST call did. Other refusals (<c>METHOD_FORBIDDEN</c> without the role
///             <c>StreamDataAdmin</c>, <c>METHOD_TARGET_NOT_FOUND</c>, <c>METHOD_TIMEOUT</c>) reach the Runner as
///             <see cref="CkMethodCallException" /> (code and parameters, exit -3).
///         </item>
///     </list>
/// </summary>
internal sealed class ArchiveMethodCall(
    ITenantClient tenantClient,
    IAuthenticationService authenticationService,
    ILogger logger)
{
    /// <summary>The <c>System.StreamData</c> version that carries every method these commands call.</summary>
    internal const string RequiredSystemStreamDataVersion = "1.30.0";

    /// <summary>The declared error of an asynchronous recompute or backfill whose job ended Failed.</summary>
    internal const string RecomputeFailedCode = "STREAMDATA_RECOMPUTE_FAILED";

    /// <summary>An operation that ended without an outcome (the asset repository restarted; queued work goes on).</summary>
    internal const string OutcomeUnknownCode = "METHOD_OUTCOME_UNKNOWN";

    private const string HandlerNotAvailableCode = "METHOD_HANDLER_NOT_AVAILABLE";

    /// <summary>The tenant client the generated method clients are created over.</summary>
    public ITenantClient TenantClient => tenantClient;

    /// <summary>Authenticates the tenant client (before a call, and before every read of a long wait).</summary>
    public Task EnsureAuthenticatedAsync() => authenticationService.EnsureAuthenticated(tenantClient.AccessToken);

    /// <summary>
    ///     The job of a recompute or backfill whose operation ended with <c>STREAMDATA_RECOMPUTE_FAILED</c>, read from the
    ///     archive's recompute jobs (a REST read that stays), so the command prints the same job line as the REST answer of a
    ///     failed job did. A job no longer listed is printed from the error parameters (<c>jobRtId</c>, <c>reason</c>).
    ///     Another declared error ends the command.
    /// </summary>
    internal static async Task<RollupRecomputeJobInfoDto> FailedJobAsync(IStreamDataServicesClient client,
        string tenantId, string archiveRtId, CkMethodError error)
    {
        if (!string.Equals(error.Code, RecomputeFailedCode, StringComparison.Ordinal) ||
            !error.Parameters.TryGetValue("jobRtId", out var jobRtId) || string.IsNullOrWhiteSpace(jobRtId))
        {
            throw new ServiceClientException(CkMethodErrorText.Format(error));
        }

        var jobs = await client.ListRecomputeJobsForArchiveAsync(tenantId, archiveRtId);
        return jobs.FirstOrDefault(j => string.Equals(j.RtId, jobRtId, StringComparison.Ordinal))
               ?? new RollupRecomputeJobInfoDto(jobRtId, "Failed", null, null, null, null, null,
                   error.Parameters.GetValueOrDefault("reason"));
    }

    /// <summary>Runs <paramref name="call" /> and returns the value of the method result; a declared error ends the command.</summary>
    /// <param name="methodId">The qualified id of the CK method, for the message when the service lacks it.</param>
    /// <param name="call">The call of the generated method client.</param>
    public async Task<TValue?> RunAsync<TValue>(string methodId, Func<Task<CkMethodResult<TValue>>> call)
    {
        var result = await InvokeAsync(methodId, call);
        if (!result.IsSuccess)
        {
            throw new ServiceClientException(CkMethodErrorText.Format(result.Error!));
        }

        return result.Value;
    }

    /// <summary>
    ///     Runs <paramref name="call" /> and returns the method result as it is (declared errors included). A missing method
    ///     ends the command with the version message.
    /// </summary>
    public async Task<CkMethodResult<TValue>> InvokeAsync<TValue>(string methodId,
        Func<Task<CkMethodResult<TValue>>> call)
    {
        await authenticationService.EnsureAuthenticated(tenantClient.AccessToken);

        try
        {
            return await call();
        }
        catch (CkMethodCallException e) when (CkMethodFallback.IsMethodUnavailable(e))
        {
            logger.LogDebug("The CK method {MethodId} is not available: {Reason}", methodId,
                CkMethodFallback.Describe(e));
            throw new ServiceClientException(MethodUnavailableMessage(methodId, tenantClient.ServiceUri?.ToString(),
                string.Equals(e.Code, HandlerNotAvailableCode, StringComparison.Ordinal)));
        }
    }

    /// <summary>
    ///     Runs an archive method (lifecycle or column) through the client of the archive's concrete type: the methods are
    ///     declared on the abstract <c>Archive</c>, and the target is looked up by the concrete type.
    /// </summary>
    public async Task<TValue?> RunOnArchiveAsync<TValue>(string archiveRtId, string methodId,
        Func<IArchiveMethodClient, Task<CkMethodResult<TValue>>> call)
    {
        var client = await ArchiveClientAsync(archiveRtId);
        return await RunAsync(methodId, () => call(client));
    }

    /// <summary>The method client of the archive's concrete type; an unknown archive ends the command before any write.</summary>
    public async Task<IArchiveMethodClient> ArchiveClientAsync(string archiveRtId)
    {
        var kind = await ResolveArchiveKindAsync(archiveRtId);
        return kind switch
        {
            ArchiveKind.Raw => new RtRawArchiveMethodClient(tenantClient),
            ArchiveKind.TimeRange => new RtTimeRangeArchiveMethodClient(tenantClient),
            _ => new RtRollupArchiveMethodClient(tenantClient)
        };
    }

    /// <summary>
    ///     The concrete archive type of <paramref name="archiveRtId" />, read with a GraphQL query of the tenant; an unknown
    ///     archive ends the command before any write.
    /// </summary>
    public async Task<ArchiveKind> ResolveArchiveKindAsync(string archiveRtId)
    {
        RequireObjectId(archiveRtId, "ArchiveRtId");
        await authenticationService.EnsureAuthenticated(tenantClient.AccessToken);

        var result = await tenantClient.SendQueryAsync<ArchiveTypeDto>(new GraphQLRequest
        {
            Query = GraphQlConstants.GetArchiveCkTypeById,
            Variables = new { rtId = archiveRtId }
        });
        var ckTypeId = result?.Items?.FirstOrDefault()?.CkTypeId;
        if (string.IsNullOrWhiteSpace(ckTypeId))
        {
            throw new ServiceClientException($"Archive '{archiveRtId}' not found.");
        }

        return ParseArchiveKind(ckTypeId) ?? throw new ServiceClientException(
            $"Archive '{archiveRtId}' is of type '{ckTypeId}', which is no RawArchive, TimeRangeArchive or RollupArchive.");
    }

    /// <summary>
    ///     The archive kind of a CK type id such as <c>System.StreamData/RollupArchive</c> or
    ///     <c>System.StreamData/RawArchive-1</c>; <c>null</c> for another type.
    /// </summary>
    internal static ArchiveKind? ParseArchiveKind(string ckTypeId)
    {
        const string prefix = "System.StreamData/";
        if (!ckTypeId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var name = ckTypeId[prefix.Length..];
        var dash = name.LastIndexOf('-');
        if (dash > 0 && dash < name.Length - 1 && name[(dash + 1)..].All(char.IsAsciiDigit))
        {
            name = name[..dash];
        }

        return name switch
        {
            "RawArchive" => ArchiveKind.Raw,
            "TimeRangeArchive" => ArchiveKind.TimeRange,
            "RollupArchive" => ArchiveKind.Rollup,
            _ => null
        };
    }

    /// <summary>
    ///     Waits for the operation of an asynchronous recompute or backfill. The tenant client's token is refreshed before
    ///     every read (a recompute can outlive an access token).
    /// </summary>
    public async Task<CkMethodResult<RtRecomputeJobInfoRecordDto?>> WaitForJobAsync(
        CkOperationHandle<RtRecomputeJobInfoRecordDto?> handle, TimeSpan timeout, TimeProvider? timeProvider = null,
        TimeSpan? pollInterval = null)
    {
        var options = new CkOperationWaitOptions
        {
            Timeout = timeout,
            BeforeReadAsync = _ => EnsureAuthenticatedAsync(),
            TimeProvider = timeProvider ?? TimeProvider.System,
            DefaultPollInterval = pollInterval ?? TimeSpan.FromSeconds(2),
            MinPollInterval = pollInterval is { } shorter && shorter < TimeSpan.FromSeconds(1)
                ? shorter
                : TimeSpan.FromSeconds(1)
        };
        return await new CkOperationsClient(tenantClient).WaitForOperationAsync(handle, options: options);
    }

    /// <summary>
    ///     The job line of a recompute job in the form the REST answer printed: the method result carries the same fields as
    ///     <see cref="RollupRecomputeJobInfoDto" />.
    /// </summary>
    internal static RollupRecomputeJobInfoDto ToJobInfo(RtRecomputeJobInfoRecordDto job) =>
        new(job.JobRtId ?? string.Empty, job.State?.ToString() ?? "Unknown",
            (int?)job.RowsProcessed, (int?)job.WindowsProcessed, job.StartedAt, job.FinishedAt, (int?)job.DurationMs,
            job.ErrorReason)
        {
            LastProgressAt = job.LastProgressAt,
            ComputedColumnName = job.ComputedColumnName
        };

    /// <summary>
    ///     The result type of a computed column (<c>Boolean</c>, <c>Int</c>, <c>Int64</c>, <c>Double</c>, <c>DateTime</c>,
    ///     case-insensitive, as the REST endpoint took it); another value ends the command before any call.
    /// </summary>
    internal static RtCkComputedColumnResultTypeEnum ParseComputedColumnResultType(string value)
    {
        // Enum.TryParse also takes numbers ("2"); the REST endpoint took the names only.
        if (!string.IsNullOrWhiteSpace(value) && !int.TryParse(value, out _) &&
            Enum.TryParse<RtCkComputedColumnResultTypeEnum>(value.Trim(), true, out var parsed) &&
            Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new ServiceClientException(
            $"Result type '{value}' is not valid. Use one of: {string.Join(", ", Enum.GetNames<RtCkComputedColumnResultTypeEnum>())}.");
    }

    /// <summary>Whether <paramref name="value" /> is a runtime id (24 hexadecimal characters).</summary>
    internal static bool IsObjectId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && OctoObjectId.TryParse(value, out _);

    /// <summary>
    ///     Ends the command before any call when <paramref name="value" /> is no runtime id. The method documents declare
    ///     the ids as <c>OctoObjectId</c>; without this check the asset repository would refuse the variable in GraphQL
    ///     validation, which reads like a service without the method.
    /// </summary>
    internal static string RequireObjectId(string value, string name) =>
        IsObjectId(value) ? value : throw new ServiceClientException($"{name} '{value}' is not a valid id.");

    /// <summary>The message of a command whose method the service does not offer; names the needed version.</summary>
    internal static string MethodUnavailableMessage(string methodId, string? serviceUri, bool noHandler = false) =>
        $"The service at '{serviceUri}' does not offer the CK method {methodId}. It needs an asset repository whose " +
        $"tenant has System.StreamData {RequiredSystemStreamDataVersion} or newer (the stream-data archive methods)" +
        (noHandler ? ", or no handler of the method is running right now (asset repository restarting?). " : ". ") +
        "Nothing was changed. octo-cli does not fall back to the REST endpoint.";

    /// <summary>The concrete types of <c>System.StreamData/Archive</c>.</summary>
    internal enum ArchiveKind
    {
        Raw,
        TimeRange,
        Rollup
    }

    /// <summary>The fields of the archive type query.</summary>
    internal sealed class ArchiveTypeDto
    {
        public string? RtId { get; set; }
        public string? CkTypeId { get; set; }
    }
}
