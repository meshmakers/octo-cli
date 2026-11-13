using FakeItEasy;
using GraphQL;
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
    ///     of the method gateway (extensions <c>code</c> and <c>parameters</c>).
    /// </summary>
    public static void AnswerMethodCalls(ITenantClient tenant, List<GraphQLRequest> requests, string? errorCode,
        string? message = null, Dictionary<string, object?>? parameters = null)
    {
        A.CallTo(tenant).Where(c => c.Method.Name == nameof(ITenantClient.SendMutationAsync))
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
                    var error = new QlQueryErrorException("GraphQL error",
                    [
                        new GraphQLError
                            { Message = message ?? $"The CK method call failed: {errorCode}", Extensions = extensions }
                    ]);
                    return FromException(responseType, error);
                }

                var response = Activator.CreateInstance(responseType)!;
                responseType.GetProperty("InvocationId")?.SetValue(response, "inv-1");
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(responseType)
                    .Invoke(null, [response])!;
            });
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

    private static object FromException(Type responseType, Exception error) =>
        typeof(Task).GetMethod(nameof(Task.FromException), 1, [typeof(Exception)])!
            .MakeGenericMethod(responseType).Invoke(null, [error])!;
}
