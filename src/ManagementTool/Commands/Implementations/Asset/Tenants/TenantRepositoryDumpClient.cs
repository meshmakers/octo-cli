using System.Text.Json.Serialization;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.Methods;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;

/// <summary>
///     AB#8446: the client of the asynchronous CK method <c>System.Bot/TenantRepository.Dump-1</c> (System.Bot 3.7.0,
///     bot services AB#8445). It has exactly the shape the SDK's source generator emits for the method
///     (<c>RtTenantRepositoryMethodClient.DumpAsync</c>, octo-sdk <c>docs/method-clients.md</c>, "Asynchronous methods"),
///     written by hand because System.Bot 3.7.0 is not yet in the CK catalog GraphQlDtos imports from
///     (<c>System.Bot-[3.0,4.0)</c>). Once it is, GraphQlDtos generates <c>RtTenantRepositoryMethodClient</c> and this
///     class can be replaced by it. <c>DumpMethodCommandTests</c> pins the document to the text the generator emits
///     (octo-sdk <c>AsyncMethodClientTests</c>, validated there against a recorded schema with System.Bot 3.7.0).
/// </summary>
internal sealed class TenantRepositoryDumpClient(ITenantClient tenantClient) : CkMethodClientBase(tenantClient)
{
    /// <summary>Qualified id of the CK method.</summary>
    public const string DumpMethodId = "System.Bot/TenantRepository.Dump-1";

    /// <summary>The GraphQL document <see cref="DumpAsync" /> sends (the generator's <c>DumpDocument</c>).</summary>
    public const string DumpDocument =
        "mutation SystemBotTenantRepositoryDump($input: SystemBotTenantRepositoryDumpMethodInput, $idempotencyKey: String) " +
        "{ runtime { systemBotTenantRepositorys { dump(input: $input, idempotencyKey: $idempotencyKey) " +
        "{ invocationId operation { operationId acceptedAtUtc retryAfterSeconds expiresAtUtc } } } } }";

    /// <summary>The declared error codes of the method.</summary>
    public static class DumpErrors
    {
        /// <summary>The dump job failed; the parameter <c>jobId</c> names the job.</summary>
        public const string DumpFailed = "DUMP_FAILED";

        /// <summary>The dump job was interrupted (the bot service stopped while it ran) and is not started again.</summary>
        public const string DumpInterrupted = "DUMP_INTERRUPTED";
    }

    private static readonly string[] DeclaredErrorCodes = [DumpErrors.DumpFailed, DumpErrors.DumpInterrupted];

    /// <summary>
    ///     Starts the dump of the tenant of the tenant client; returns the operation handle at once.
    /// </summary>
    /// <param name="includeArchiveData">Also bundle the archive (stream data) rows into an .octobak.zip container</param>
    /// <param name="idempotencyKey">Optional idempotency key</param>
    public Task<CkMethodResult<CkOperationHandle<RepositoryDumpResult?>>> DumpAsync(bool? includeArchiveData = null,
        string? idempotencyKey = null) =>
        StartCkOperationAsync<DumpResponse, RepositoryDumpResult?>(DumpMethodId, DumpDocument,
            "SystemBotTenantRepositoryDump",
            new DumpVariables { Input = new DumpInput { IncludeArchiveData = includeArchiveData }, IdempotencyKey = idempotencyKey },
            DeclaredErrorCodes);

    private sealed class DumpVariables
    {
        [JsonPropertyName("input")]
        public DumpInput? Input { get; set; }

        [JsonPropertyName("idempotencyKey")]
        public string? IdempotencyKey { get; set; }
    }

    private sealed class DumpInput
    {
        [JsonPropertyName("includeArchiveData")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? IncludeArchiveData { get; set; }
    }

    private sealed class DumpResponse : CkMethodAsyncCallResponse
    {
    }
}

/// <summary>
///     The result record <c>System.Bot/RepositoryDumpResult</c>: the job that produced the dump and where it is downloaded.
/// </summary>
internal sealed class RepositoryDumpResult
{
    /// <summary>Id of the Hangfire job that produced the dump (the download addresses it).</summary>
    public string? JobId { get; set; }

    /// <summary>Relative download path on the bot service, <c>{tenantId}/v1/jobs/download?id={jobId}</c>.</summary>
    public string? DownloadPath { get; set; }
}
