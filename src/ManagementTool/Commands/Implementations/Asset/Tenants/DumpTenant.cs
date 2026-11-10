using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Operations;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.Methods;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;

/// <summary>
///     Dumps a tenant to a file. AB#8446: in the tenant of the context the dump runs through the asynchronous CK method
///     <c>System.Bot/TenantRepository.Dump-1</c> (operation handle, progress from <c>runtime.operations</c>); it falls back
///     to the job API (<c>system/v1/jobs</c>, as before) when the service lacks the method, and always for another tenant
///     (the method runs only with a token of its own tenant). The download is the same in both cases.
/// </summary>
internal class DumpTenant : JobOctoCommand
{
    private readonly IArgument _tenantIdArg;
    private readonly IArgument _fileArg;
    private readonly IArgument _includeArchiveDataArg;
    private readonly ITenantClient _tenantClient;

    public DumpTenant(ILogger<DumpTenant> logger, IOptions<OctoToolOptions> options,
        IBotServicesClient botServicesClient, IAuthenticationService authenticationService, ITenantClient tenantClient)
        : base(logger, Constants.BotServicesGroup, "Dump", "Dumps a tenant to a file",
            options, botServicesClient,
            authenticationService)
    {
        _tenantClient = tenantClient;
        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId", ["Id of tenant"],
            true, 1);
        _fileArg = CommandArgumentValue.AddArgument("f", "file", ["File of backup (*.tar.gz, or *.octobak.zip with --include-archive-data)"], true, 1);
        _includeArchiveDataArg = CommandArgumentValue.AddArgument("iad", "include-archive-data",
            ["Include CrateDB archive data in the dump. The produced backup is a larger '*.octobak.zip' artifact"],
            false, 0);
    }

    /// <summary>The time provider of the wait for the dump operation (tests).</summary>
    internal TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_tenantIdArg, "mytenant"),
                    new CodeSampleArgument(_fileArg, "./backup.tar.gz"),
                ],
                    description: "Basic usage"),
                new CodeSample(arguments: [
                    new CodeSampleArgument(_tenantIdArg, "mytenant"),
                    new CodeSampleArgument(_fileArg, "./backup.octobak.zip"),
                    new CodeSampleArgument(_includeArchiveDataArg),
                ],
                    description: "Include CrateDB archive data (produces a larger '*.octobak.zip' artifact)"),
            ],
            Notes:
            [
                "Without --include-archive-data the backup is a plain '*.tar.gz' (MongoDB data only).",
                "With --include-archive-data the produced artifact is a larger '*.octobak.zip' that also carries the CrateDB archive rows; pick a matching file name with -f.",
                "The command always waits for the dump and then downloads the file.",
                "For the tenant of the context the dump runs as the asynchronous CK method System.Bot/TenantRepository.Dump-1: the command prints the operation id and the progress (stage, percent, job id) and reads the result through the operation handle. The operation can be read again for 7 days with GetOperation and the operation id.",
                "The command uses the job API instead (as in earlier versions) when the asset repository or the bot service does not offer the dump method yet, and for a tenant other than the tenant of the context (for example a child tenant dumped with the parent's context).",
            ]
        );

    public override async Task Execute()
    {
        var tenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg).ToLower();
        var filePath = CommandArgumentValue.GetArgumentScalarValue<string>(_fileArg);
        var includeArchiveData = CommandArgumentValue.IsArgumentUsed(_includeArchiveDataArg);

        Logger.LogInformation(
            "Create dump of tenant \'{TenantId}\' at \'{ServiceClientServiceUri}\' (include archive data: {IncludeArchiveData})",
            tenantId, ServiceClient.ServiceUri, includeArchiveData);

        if (string.IsNullOrEmpty(tenantId))
        {
            throw ToolException.NoTenantIdConfigured();
        }

        var jobId = await TryDumpThroughMethodAsync(tenantId, includeArchiveData) ??
                    await DumpThroughJobAsync(tenantId, includeArchiveData);

        Logger.LogInformation(
            "Dump of tenant \'{TenantId}\' at \'{ServiceClientServiceUri}\' created. Downloading...", tenantId, ServiceClient.ServiceUri);
        await ServiceClient.DownloadDumpToFileAsync(tenantId, jobId, filePath,
            bytesDownloaded =>
            {
                Logger.LogInformation("Downloaded {Bytes:N0} bytes", bytesDownloaded);
            });
        Logger.LogInformation("Dump downloaded to \'{FilePath}\'", filePath);
    }

    internal static string? DescribeDeclaredError(string code) => code switch
    {
        TenantRepositoryDumpClient.DumpErrors.DumpFailed =>
            "The dump job failed; the job status (system/v1/jobs) shows its error.",
        TenantRepositoryDumpClient.DumpErrors.DumpInterrupted =>
            "The dump job was interrupted (the bot service stopped while it ran) and is not started again.",
        _ => null
    };

    /// <summary>The job path of earlier versions: start the job over REST and poll <c>system/v1/jobs</c>.</summary>
    private async Task<string> DumpThroughJobAsync(string tenantId, bool includeArchiveData)
    {
        var response = await ServiceClient.StartDumpRepositoryAsync(tenantId, includeArchiveData);
        Logger.LogInformation("Create dump with job id \'{Id}\' has been started", response.JobId);
        await WaitForJob(response.JobId);
        return response.JobId;
    }

    /// <summary>
    ///     Runs the dump through the CK method and returns the job id of the result, or <c>null</c> when the job path has to
    ///     be used (another tenant, or the method is not available).
    /// </summary>
    private async Task<string?> TryDumpThroughMethodAsync(string tenantId, bool includeArchiveData)
    {
        if (!string.Equals(tenantId, Options.Value.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogInformation(
                "Tenant \'{TenantId}\' is not the tenant of the context (\'{ContextTenantId}\'); the dump uses the job API",
                tenantId, Options.Value.TenantId);
            return null;
        }

        // The tenant client has its own access token; PreValidate only authenticated the bot client.
        await AuthenticationService.EnsureAuthenticated(_tenantClient.AccessToken);

        CkMethodResult<CkOperationHandle<RepositoryDumpResult?>> accepted;
        try
        {
            accepted = await new TenantRepositoryDumpClient(_tenantClient).DumpAsync(includeArchiveData);
        }
        catch (CkMethodCallException e) when (DumpMethodFallback.IsMethodUnavailable(e))
        {
            Logger.LogInformation(
                "The dump method {MethodId} is not available ({Reason}); the dump uses the job API",
                TenantRepositoryDumpClient.DumpMethodId, DumpMethodFallback.Describe(e));
            return null;
        }

        if (!accepted.IsSuccess)
        {
            // A declared error before the work started (e.g. DUMP_FAILED with reason another-dump-running).
            throw new ServiceClientException(CkMethodErrorText.Format(accepted.Error!,
                DescribeDeclaredError(accepted.Error!.Code)));
        }

        var handle = accepted.Value!;
        Logger.LogInformation("Dump operation \'{OperationId}\' accepted ({MethodId})", handle.OperationId,
            TenantRepositoryDumpClient.DumpMethodId);

        var result = await new CkOperationsClient(_tenantClient).WaitForOperationAsync(handle,
            new OperationProgressLogger(Logger, "Dump operation"),
            new CkOperationWaitOptions
            {
                TimeProvider = TimeProvider,
                // A dump can outlive the access token: refresh it before every read (expiry-gated, cheap; AB#4755).
                BeforeReadAsync = _ => AuthenticationService.EnsureAuthenticated(_tenantClient.AccessToken)
            });

        if (!result.IsSuccess)
        {
            throw new ServiceClientException(CkMethodErrorText.Format(result.Error!,
                DescribeDeclaredError(result.Error!.Code)));
        }

        var jobId = result.Value?.JobId;
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ServiceClientException(
                $"The dump operation '{handle.OperationId}' succeeded without a job id; the file cannot be downloaded.");
        }

        Logger.LogInformation("Dump operation \'{OperationId}\' succeeded with job id \'{JobId}\'", handle.OperationId,
            jobId);
        return jobId;
    }
}

/// <summary>
///     AB#8446: when the dump command falls back to the job API. Only when the call was certainly not executed: the
///     asset repository rejected the document before execution (GraphQL validation, e.g. an older asset repository or a
///     tenant without System.Bot 3.7.0 - every error carries the spec section <c>extensions.number</c>), or no handler
///     of the method is live (<c>METHOD_HANDLER_NOT_AVAILABLE</c>, e.g. a bot service without the method).
/// </summary>
internal static class DumpMethodFallback
{
    private const string HandlerNotAvailable = "METHOD_HANDLER_NOT_AVAILABLE";

    public static bool IsMethodUnavailable(CkMethodCallException exception) =>
        string.Equals(exception.Code, HandlerNotAvailable, StringComparison.Ordinal) || IsValidationError(exception);

    public static string Describe(CkMethodCallException exception) =>
        IsValidationError(exception)
            ? $"the asset repository does not know it: {string.Join(", ", (exception.Errors ?? []).Select(e => e.Message))}"
            : CkMethodErrorText.Format(exception);

    private static bool IsValidationError(CkMethodCallException exception) =>
        exception.Errors is { Count: > 0 } errors &&
        errors.All(e => e.Extensions != null && e.Extensions.ContainsKey("number"));
}
