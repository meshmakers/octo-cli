using FakeItEasy;
using GraphQL;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;

namespace ManagementTool.Tests;

/// <summary>
///     Answers the <c>SendMutationAsync</c> calls a generated CK method client makes through a faked
///     <see cref="ITenantClient" />, in the shapes the method gateway of the asset repository sends.
/// </summary>
internal static class MethodFakes
{
    /// <summary>
    ///     Success (a response with an invocation id) when <paramref name="errorCode" /> is null, otherwise the GraphQL error
    ///     of the method gateway (extensions <c>code</c> and <c>parameters</c>). With <paramref name="once" /> the answer
    ///     applies to the next call only and the earlier configuration answers the calls after it. <paramref name="results" />
    ///     are candidates for several commands: each call answers the first one of its result type.
    /// </summary>
    public static void AnswerMethodCalls(ITenantClient tenant, List<GraphQLRequest> requests, string? errorCode,
        string? message = null, Dictionary<string, object?>? parameters = null, IReadOnlyList<string>? detailCodes = null,
        object? result = null, bool once = false, IReadOnlyList<object>? results = null)
    {
        var answer = A.CallTo(tenant).Where(c => c.Method.Name == nameof(ITenantClient.SendMutationAsync))
            .WithNonVoidReturnType()
            .ReturnsLazily(call =>
            {
                requests.Add((GraphQLRequest)call.Arguments[0]!);
                var responseType = call.Method.ReturnType.GetGenericArguments()[0];
                if (errorCode != null)
                {
                    var extensions = new Map
                    {
                        ["code"] = errorCode,
                        ["parameters"] = new Dictionary<string, object?>(parameters ?? [])
                        {
                            ["methodId"] = "System.Identity/User.Method-1"
                        }
                    };
                    if (detailCodes is { Count: > 0 })
                    {
                        extensions["details"] = detailCodes
                            .Select(c => (object?)new Dictionary<string, object?>
                                { ["code"] = c, ["parameters"] = new Dictionary<string, object?>() })
                            .ToList();
                    }

                    var error = new QlQueryErrorException("GraphQL error",
                    [
                        new GraphQLError
                            { Message = message ?? $"The CK method call failed: {errorCode}", Extensions = extensions }
                    ]);
                    return FromException(responseType, error);
                }

                var response = Activator.CreateInstance(responseType)!;
                responseType.GetProperty("InvocationId")?.SetValue(response, "inv-1");
                // The method's result (a record, a created rtId or a secret), when the test gives one of the right type.
                var resultProperty = responseType.GetProperty("Result");
                var answered = result ?? results?.FirstOrDefault(r => resultProperty?.PropertyType.IsInstanceOfType(r) == true);
                if (answered != null && resultProperty != null && resultProperty.PropertyType.IsInstanceOfType(answered))
                {
                    resultProperty.SetValue(response, answered);
                }

                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(responseType)
                    .Invoke(null, [response])!;
            });
        if (once)
        {
            answer.Once();
        }
    }

    /// <summary>
    ///     The GraphQL validation errors of a server whose schema lacks the method (asset repository or tenant CK model
    ///     without it): every error carries the spec section <c>extensions.number</c>.
    /// </summary>
    public static void AnswerMethodCallsWithValidationErrors(ITenantClient tenant, List<GraphQLRequest> requests,
        string inputType, string field)
    {
        A.CallTo(tenant).Where(c => c.Method.Name == nameof(ITenantClient.SendMutationAsync))
            .WithNonVoidReturnType()
            .ReturnsLazily(call =>
            {
                requests.Add((GraphQLRequest)call.Arguments[0]!);
                var responseType = call.Method.ReturnType.GetGenericArguments()[0];
                GraphQLError Validation(string text, string code, string number) => new()
                {
                    Message = text,
                    Extensions = new Map { ["code"] = code, ["codes"] = new List<object> { code }, ["number"] = number }
                };
                var error = new QlQueryErrorException("GraphQL error",
                [
                    Validation($"Variable 'input' cannot be non-input type '{inputType}'.", "VARIABLES_ARE_INPUT_TYPES", "5.8.2"),
                    Validation($"Unknown type {inputType}.", "KNOWN_TYPE_NAMES", "5.5.1.2"),
                    Validation($"Cannot query field '{field}' on type 'SystemIdentityUserMutations'.", "FIELDS_ON_CORRECT_TYPE", "5.3.1")
                ]);
                return FromException(responseType, error);
            });
    }

    /// <summary>
    ///     A tenant client of a server without the identity methods (GraphQL validation errors), so a command takes the REST
    ///     path of earlier identity versions. For the tests of the REST answers that are kept as the fallback.
    /// </summary>
    public static ITenantClient TenantWithoutMethods()
    {
        var tenant = A.Fake<ITenantClient>();
        AnswerMethodCallsWithValidationErrors(tenant, [], "SystemIdentityMethodInput", "method");
        return tenant;
    }

    /// <summary>The runtime id the faked lookups below answer for every client, API resource and API scope.</summary>
    public const string TargetRtId = "65f0c0ffee000000000000aa";

    /// <summary>
    ///     A tenant client of a server with the identity methods: every method call succeeds (recorded in
    ///     <paramref name="requests" />, answering <paramref name="result" /> when its type fits), and the runtime id lookups
    ///     of API resources and API scopes answer <see cref="TargetRtId" />.
    /// </summary>
    public static ITenantClient IdentityTenant(List<GraphQLRequest> requests, object? result = null)
    {
        var tenant = A.Fake<ITenantClient>();
        AnswerMethodCalls(tenant, requests, null, result: result);
        A.CallTo(() => tenant.SendQueryAsync<RtApiResourceDto>(A<GraphQLRequest>._)).Returns(
            Task.FromResult<QlItemsContainer<RtApiResourceDto>?>(new QlItemsContainer<RtApiResourceDto>
                { Items = [new RtApiResourceDto { RtId = OctoObjectId.Parse(TargetRtId) }] }));
        A.CallTo(() => tenant.SendQueryAsync<RtApiScopeDto>(A<GraphQLRequest>._)).Returns(
            Task.FromResult<QlItemsContainer<RtApiScopeDto>?>(new QlItemsContainer<RtApiScopeDto>
                { Items = [new RtApiScopeDto { RtId = OctoObjectId.Parse(TargetRtId) }] }));
        return tenant;
    }

    /// <summary>Lets <c>GetClient</c> of <paramref name="identity" /> answer every client with <see cref="TargetRtId" />.</summary>
    public static void GiveClientsRuntimeIds(IIdentityServicesClient identity) =>
        A.CallTo(() => identity.GetClient(A<string>._)).ReturnsLazily((string id) =>
            Task.FromResult(new ClientDto { ClientId = id, RtId = TargetRtId }));

    /// <summary>The variables of a method call as JSON (where a secret parameter travels).</summary>
    public static string VariablesOf(GraphQLRequest request) => System.Text.Json.JsonSerializer.Serialize(request.Variables);

    private static object FromException(Type responseType, Exception error) =>
        typeof(Task).GetMethod(nameof(Task.FromException), 1, [typeof(Exception)])!
            .MakeGenericMethod(responseType).Invoke(null, [error])!;
}
