using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.Methods;

namespace GraphQlDtos.DataTransferObjects.System.StreamData.v1;

/// <summary>
///     The methods of the abstract <c>System.StreamData/Archive</c> (lifecycle and columns) as one interface. The generator
///     emits them on the client of every concrete archive type (<see cref="RtRawArchiveMethodClient" />,
///     <see cref="RtTimeRangeArchiveMethodClient" />, <see cref="RtRollupArchiveMethodClient" />) with the same signatures;
///     the partial declarations below make each client implement it, so a command picks the client of the archive's
///     concrete type once and calls the method through the interface.
/// </summary>
public interface IArchiveMethodClient
{
    Task<CkMethodResult<RtArchiveTransitionResultRecordDto?>> ActivateAsync(string rtId, string? idempotencyKey = null);

    Task<CkMethodResult<RtArchiveTransitionResultRecordDto?>> DisableAsync(string rtId, string? idempotencyKey = null);

    Task<CkMethodResult<RtArchiveTransitionResultRecordDto?>> EnableAsync(string rtId, string? idempotencyKey = null);

    Task<CkMethodResult<RtArchiveTransitionResultRecordDto?>> RetryActivationAsync(string rtId,
        string? idempotencyKey = null);

    Task<CkMethodResult<bool?>> DeleteArchiveAsync(string rtId, string? idempotencyKey = null);

    Task<CkMethodResult<RtArchiveTransitionResultRecordDto?>> AddColumnAsync(string rtId, string path,
        bool? indexed = null, string? idempotencyKey = null);

    Task<CkMethodResult<RtArchiveTransitionResultRecordDto?>> AddComputedColumnAsync(string rtId, string name,
        string formula, RtCkComputedColumnResultTypeEnum resultType, bool? indexed = null, string? idempotencyKey = null);

    Task<CkMethodResult<RtArchiveTransitionResultRecordDto?>> RemoveComputedColumnAsync(string rtId, string name,
        string? idempotencyKey = null);

    Task<CkMethodResult<RtArchiveTransitionResultRecordDto?>> UpdateComputedColumnFormulaAsync(string rtId, string name,
        string formula, string? idempotencyKey = null);

    Task<CkMethodResult<RtRecomputeJobInfoRecordDto?>> RetryComputedColumnBackfillAsync(string rtId, string name,
        string? idempotencyKey = null);
}

public partial class RtRawArchiveMethodClient : IArchiveMethodClient;

public partial class RtTimeRangeArchiveMethodClient : IArchiveMethodClient;

public partial class RtRollupArchiveMethodClient : IArchiveMethodClient;
