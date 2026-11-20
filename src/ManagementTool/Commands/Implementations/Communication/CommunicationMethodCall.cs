using GraphQL;
using GraphQlDtos;
using GraphQlDtos.DataTransferObjects.System.Communication.v4;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.Methods;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

/// <summary>
///     Runs one communication command through a CK method client of <c>System.Communication</c> (communication wave 3:
///     pipeline deploy, execute, debug and move, data flow, workload, deployment site and trigger deploy/undeploy, workload
///     chart version, queued execution cancel, service account secret rotation), generated in GraphQlDtos from
///     <c>System.Communication-[4.19,5.0)</c>. The method call goes to the asset repository of the context's tenant; the
///     communication controller runs the handler.
///     <list type="bullet">
///         <item>
///             The tenant client is authenticated first: it carries its own access token, the command's
///             <c>PreValidate</c> authenticated only the communication client.
///         </item>
///         <item>
///             There is <b>no REST fallback</b>: these commands need communication controller services with the wave 3
///             methods (<c>System.Communication</c> <see cref="RequiredSystemCommunicationVersion" />). A server without the
///             method - the asset repository rejects the document in GraphQL validation, or no handler of the method is live
///             (<c>METHOD_HANDLER_NOT_AVAILABLE</c>) - ends the command with a <see cref="ServiceClientException" /> that
///             names the needed version (exit -3). Nothing was changed in that case.
///         </item>
///         <item>
///             A declared error of the method (e.g. <c>WORKLOAD_DEPLOY_FAILED</c>) ends the command with a
///             <see cref="ServiceClientException" /> <c>CODE: message (parameters)</c>; the Runner logs it and the command
///             exits with -3, as the refused REST call did. Other refusals (<c>METHOD_FORBIDDEN</c> without the role
///             <c>CommunicationManagement</c>, <c>METHOD_VALIDATION_FAILED</c>, <c>METHOD_TARGET_NOT_FOUND</c>,
///             <c>METHOD_TIMEOUT</c>) reach the Runner as <see cref="CkMethodCallException" /> (code and parameters,
///             exit -3).
///         </item>
///     </list>
/// </summary>
internal sealed class CommunicationMethodCall(
    ITenantClient tenantClient,
    IAuthenticationService authenticationService,
    ILogger logger)
{
    /// <summary>The <c>System.Communication</c> version that carries every method these commands call.</summary>
    internal const string RequiredSystemCommunicationVersion = "4.19.0";

    /// <summary>The platform error code of a call whose target entity does not exist (or is of another type).</summary>
    internal const string TargetNotFoundCode = "METHOD_TARGET_NOT_FOUND";

    /// <summary>The platform error code of a refused caller (e.g. without the role <c>CommunicationManagement</c>).</summary>
    internal const string ForbiddenCode = "METHOD_FORBIDDEN";

    private const string HandlerNotAvailableCode = "METHOD_HANDLER_NOT_AVAILABLE";

    /// <summary>The tenant client the generated method clients are created over.</summary>
    public ITenantClient TenantClient => tenantClient;

    /// <summary>Runs <paramref name="call" />.</summary>
    /// <param name="methodId">The qualified id of the CK method, for the message when the service lacks it.</param>
    /// <param name="call">The call of the generated method client.</param>
    /// <param name="declaredErrorOutcome">See <see cref="RunAsync{TValue}" />.</param>
    public async Task RunAsync(string methodId, Func<Task<CkMethodResult>> call,
        Func<CkMethodError, bool>? declaredErrorOutcome = null)
    {
        await RunAsync<object>(methodId, async () =>
        {
            var result = await call();
            return result.IsSuccess
                ? CkMethodResult<object>.Success(result.InvocationId, null)
                : CkMethodResult<object>.Failure(result.Error!);
        }, declaredErrorOutcome);
    }

    /// <summary>Runs <paramref name="call" /> and returns the value of the method result.</summary>
    /// <param name="methodId">The qualified id of the CK method, for the message when the service lacks it.</param>
    /// <param name="call">The call of the generated method client.</param>
    /// <param name="declaredErrorOutcome">
    ///     Declared errors the command reports as an outcome of its own instead of a failure (the queued execution cancel:
    ///     <c>EXECUTION_ALREADY_LEASED</c>, <c>QUEUED_EXECUTION_NOT_FOUND</c>); returns <c>true</c> when it handled the
    ///     error, the command then ends as before (exit 0).
    /// </param>
    public async Task<TValue?> RunAsync<TValue>(string methodId, Func<Task<CkMethodResult<TValue>>> call,
        Func<CkMethodError, bool>? declaredErrorOutcome = null)
    {
        var result = await InvokeAsync(methodId, call);
        if (!result.IsSuccess && declaredErrorOutcome?.Invoke(result.Error!) == true)
        {
            return default;
        }

        if (!result.IsSuccess)
        {
            throw new ServiceClientException(CkMethodErrorText.Format(result.Error!));
        }

        return result.Value;
    }

    /// <summary>
    ///     Runs <paramref name="call" /> of a method that always returns a result record and returns it. A success without
    ///     a result ends the command (exit -3): the REST call either returned its answer or failed, and the command must not
    ///     print an empty execution id or a rotation without its message.
    /// </summary>
    public async Task<TValue> RunWithResultAsync<TValue>(string methodId, Func<Task<CkMethodResult<TValue?>>> call)
        where TValue : class =>
        await RunAsync(methodId, call) ??
        throw new ServiceClientException($"The CK method {methodId} returned no result.");

    /// <summary>
    ///     Runs <paramref name="call" /> and returns the method result as it is (declared errors included), for commands
    ///     that report each call of a list on its own (the pipeline move). A missing method still ends the command with the
    ///     version message.
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
    /// <param name="methodId">The qualified id of the CK method.</param>
    /// <param name="serviceUri">The asset repository the call went to.</param>
    /// <param name="noHandler">
    ///     The cause was <c>METHOD_HANDLER_NOT_AVAILABLE</c>: the model declares the method but no handler is live, which
    ///     also happens while the communication controller restarts.
    /// </param>
    internal static string MethodUnavailableMessage(string methodId, string? serviceUri, bool noHandler = false) =>
        $"The service at '{serviceUri}' does not offer the CK method {methodId}. It needs communication controller " +
        $"services with System.Communication {RequiredSystemCommunicationVersion} or newer (the communication wave 3 " +
        "methods)" +
        (noHandler ? ", or no handler of the method is running right now (communication controller restarting?). " : ". ") +
        "Nothing was changed. octo-cli does not fall back to the REST endpoint.";

    /// <summary>
    ///     The concrete workload type of <paramref name="workloadRtId" /> (<c>Adapter</c>, <c>AdapterPool</c> or
    ///     <c>Application</c>): the workload methods are declared on the abstract <c>DeployableWorkload</c> and run through
    ///     the client of the workload's concrete type. Read with a GraphQL query of the tenant; an unknown workload ends the
    ///     command before any write.
    /// </summary>
    public async Task<WorkloadKind> ResolveWorkloadKindAsync(string workloadRtId)
    {
        RequireObjectId(workloadRtId, "WorkloadRtId");
        await authenticationService.EnsureAuthenticated(tenantClient.AccessToken);

        var result = await tenantClient.SendQueryAsync<WorkloadTypeDto>(new GraphQLRequest
        {
            Query = GraphQlConstants.GetDeployableWorkloadCkTypeById,
            Variables = new { rtId = workloadRtId }
        });
        var ckTypeId = result?.Items?.FirstOrDefault()?.CkTypeId;
        if (string.IsNullOrWhiteSpace(ckTypeId))
        {
            throw new ServiceClientException($"Workload '{workloadRtId}' not found.");
        }

        return ParseWorkloadKind(ckTypeId) ?? throw new ServiceClientException(
            $"Workload '{workloadRtId}' is of type '{ckTypeId}', which is no Adapter, AdapterPool or Application.");
    }

    /// <summary>
    ///     The workload kind of a CK type id such as <c>System.Communication/Adapter</c> or
    ///     <c>System.Communication/AdapterPool-1</c>; <c>null</c> for another type.
    /// </summary>
    internal static WorkloadKind? ParseWorkloadKind(string ckTypeId)
    {
        const string prefix = "System.Communication/";
        if (!ckTypeId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var name = ckTypeId[prefix.Length..];
        var dash = name.LastIndexOf('-');
        if (dash > 0 && name[(dash + 1)..].All(char.IsAsciiDigit) && dash < name.Length - 1)
        {
            name = name[..dash];
        }

        return name switch
        {
            "Adapter" => WorkloadKind.Adapter,
            "AdapterPool" => WorkloadKind.AdapterPool,
            "Application" => WorkloadKind.Application,
            _ => null
        };
    }

    /// <summary>Deploys the workload through the client of its concrete type; the result's warning, if any.</summary>
    public async Task<string?> DeployWorkloadAsync(string workloadRtId)
    {
        var kind = await ResolveWorkloadKindAsync(workloadRtId);
        var result = await RunAsync(RtAdapterMethodClient.DeployMethodId, () => kind switch
        {
            WorkloadKind.Adapter => new RtAdapterMethodClient(tenantClient).DeployAsync(workloadRtId),
            WorkloadKind.AdapterPool => new RtAdapterPoolMethodClient(tenantClient).DeployAsync(workloadRtId),
            _ => new RtApplicationMethodClient(tenantClient).DeployAsync(workloadRtId)
        });
        return result?.Warning;
    }

    /// <summary>Undeploys the workload through the client of its concrete type.</summary>
    public async Task UndeployWorkloadAsync(string workloadRtId)
    {
        var kind = await ResolveWorkloadKindAsync(workloadRtId);
        await RunAsync(RtAdapterMethodClient.UndeployMethodId, () => kind switch
        {
            WorkloadKind.Adapter => new RtAdapterMethodClient(tenantClient).UndeployAsync(workloadRtId),
            WorkloadKind.AdapterPool => new RtAdapterPoolMethodClient(tenantClient).UndeployAsync(workloadRtId),
            _ => new RtApplicationMethodClient(tenantClient).UndeployAsync(workloadRtId)
        });
    }

    /// <summary>Sets the chart version of the workload through the client of its concrete type.</summary>
    public async Task SetWorkloadChartVersionAsync(string workloadRtId, string chartVersion)
    {
        var kind = await ResolveWorkloadKindAsync(workloadRtId);
        await RunAsync(RtAdapterMethodClient.SetChartVersionMethodId, () => kind switch
        {
            WorkloadKind.Adapter => new RtAdapterMethodClient(tenantClient).SetChartVersionAsync(workloadRtId,
                chartVersion),
            WorkloadKind.AdapterPool => new RtAdapterPoolMethodClient(tenantClient).SetChartVersionAsync(workloadRtId,
                chartVersion),
            _ => new RtApplicationMethodClient(tenantClient).SetChartVersionAsync(workloadRtId, chartVersion)
        });
    }

    /// <summary>The concrete types of <c>System.Communication/DeployableWorkload</c>.</summary>
    internal enum WorkloadKind
    {
        Adapter,
        AdapterPool,
        Application
    }

    /// <summary>The fields of the workload type query.</summary>
    internal sealed class WorkloadTypeDto
    {
        public string? RtId { get; set; }
        public string? CkTypeId { get; set; }
    }
}
