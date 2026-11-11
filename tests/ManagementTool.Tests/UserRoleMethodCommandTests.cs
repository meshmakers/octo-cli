using System.Reflection;
using FakeItEasy;
using GraphQL;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Users;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.Methods;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#8417 — AddUserToRole and RemoveUserFromRole run the CK methods <c>System.Identity/User.AddRole-1</c> /
///     <c>User.RemoveRole-1</c> through the generated <c>RtUserMethodClient</c> (GraphQlDtos, System.Identity-[3.2,4.0)):
///     the user is addressed by runtime id, the obsolete REST methods are not called, the output is unchanged, and a
///     refused call names the declared error code and the message the REST endpoint answered before. AB#8838: a server
///     without the methods (released 3.5.x before identity 3.5.52) gets the REST call; an unknown role fails the command.
/// </summary>
public class UserRoleMethodCommandTests
{
    private const string UserRtId = "65f0c0ffee0000000000abcd";

    private readonly IIdentityServicesClient _identity = A.Fake<IIdentityServicesClient>();
    private readonly ITenantClient _tenant = A.Fake<ITenantClient>();
    private readonly IAuthenticationService _auth = A.Fake<IAuthenticationService>();
    private readonly IConfirmationService _confirmation = A.Fake<IConfirmationService>();
    private readonly List<GraphQLRequest> _requests = [];

    public UserRoleMethodCommandTests()
    {
        A.CallTo(() => _identity.GetUserByNameEmailOrId(A<string>._)).ReturnsLazily((string name) =>
            Task.FromResult(new UserDto { Name = name, UserId = UserRtId }));
        A.CallTo(() => _identity.GetRoleByName(A<string>._)).ReturnsLazily((string name) =>
            Task.FromResult(new RoleDto { Name = name }));
        AnswerMethodCalls(null);
    }

    [Fact]
    public async Task AddUserToRole_RunsTheAddRoleMethod_ByRtId_WithUnchangedOutput()
    {
        var logger = new RecordingLogger<AddUserToRole>();
        var command = Add(logger, "-un", "John.Doe", "-r", "DataAnalyst");

        await command.Execute();

        var request = Assert.Single(_requests);
        Assert.Contains("addRole", request.Query, StringComparison.Ordinal);
        var variables = Variables(request);
        Assert.Contains(UserRtId, variables);
        Assert.Contains("dataanalyst", variables);
        A.CallTo(() => _identity.GetUserByNameEmailOrId("john.doe")).MustHaveHappenedOnceExactly();
        A.CallTo(() => _auth.EnsureAuthenticated(_tenant.AccessToken)).MustHaveHappened();
        AssertNoRestRoleCall();
        Assert.Collection(logger.Entries,
            e => Assert.StartsWith("Adding user 'john.doe' to role 'dataanalyst' at ", e.Message),
            e => Assert.Equal("User 'john.doe' updated", e.Message));
    }

    [Fact]
    public async Task AddUserToRole_DeclaredError_PrintsCodeAndTheRestMessage()
    {
        AnswerMethodCalls("ROLE_ALREADY_ASSIGNED");
        var logger = new RecordingLogger<AddUserToRole>();
        var command = Add(logger, "-un", "john.doe", "-r", "DataAnalyst");

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() => command.Execute());

        Assert.Equal("ROLE_ALREADY_ASSIGNED: User 'john.doe' already has role 'dataanalyst'", ex.Message);
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("updated"));
    }

    [Fact]
    public async Task AddUserToRole_UnknownRole_StopsBeforeTheMethodCall_AndFails()
    {
        A.CallTo(() => _identity.GetRoleByName(A<string>._))
            .ThrowsAsync(new ServiceClientResultException("not found", System.Net.HttpStatusCode.NotFound));
        var logger = new RecordingLogger<AddUserToRole>();
        var command = Add(logger, "-un", "john.doe", "-r", "Nope");

        // AB#8838: a ServiceClientException, so the Runner exits with -3 instead of 0.
        var ex = await Assert.ThrowsAsync<ServiceClientException>(() => command.Execute());

        Assert.StartsWith("Role 'nope' does not exist at Service", ex.Message);
        Assert.Empty(_requests);
        AssertNoRestRoleCall();
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("updated"));
    }

    [Fact]
    public async Task AddUserToRole_ServerWithoutTheMethod_FallsBackToRest()
    {
        // AB#8838: the validation answer of a 3.5.23 server (recorded read-only on the FH lab installation).
        AnswerMethodCallsWithValidationErrors("SystemIdentityUserAddRoleMethodInput", "addRole");
        var logger = new RecordingLogger<AddUserToRole>();
        var command = Add(logger, "-un", "John.Doe", "-r", "StreamDataReader");

        await command.Execute();

        Assert.Single(_requests);
#pragma warning disable CS0618 // the obsolete REST method is the expected fallback
        A.CallTo(() => _identity.AddRoleToUser("john.doe", "streamdatareader")).MustHaveHappenedOnceExactly();
#pragma warning restore CS0618
        Assert.Collection(logger.Entries,
            e => Assert.StartsWith("Adding user 'john.doe' to role 'streamdatareader' at ", e.Message),
            e => Assert.StartsWith(
                "The CK method System.Identity/User.AddRole-1 is not available (the asset repository does not know it: ",
                e.Message),
            e => Assert.Equal("User 'john.doe' updated", e.Message));
    }

    [Fact]
    public async Task RemoveUserFromRole_ServerWithoutTheMethod_FallsBackToRest()
    {
        AnswerMethodCallsWithValidationErrors("SystemIdentityUserRemoveRoleMethodInput", "removeRole");
        var command = Remove(new RecordingLogger<RemoveUserFromRole>(), "-un", "john.doe", "-r", "DataAnalyst", "-y");

        await command.Execute();

#pragma warning disable CS0618 // the obsolete REST method is the expected fallback
        A.CallTo(() => _identity.RemoveRoleFromUser("john.doe", "dataanalyst")).MustHaveHappenedOnceExactly();
#pragma warning restore CS0618
    }

    [Fact]
    public async Task AddUserToRole_NoLiveHandler_FallsBackToRest()
    {
        AnswerMethodCalls("METHOD_HANDLER_NOT_AVAILABLE", new Dictionary<string, object?> { ["reason"] = "no-live-handler" });
        var command = Add(new RecordingLogger<AddUserToRole>(), "-un", "john.doe", "-r", "DataAnalyst");

        await command.Execute();

#pragma warning disable CS0618 // the obsolete REST method is the expected fallback
        A.CallTo(() => _identity.AddRoleToUser("john.doe", "dataanalyst")).MustHaveHappenedOnceExactly();
#pragma warning restore CS0618
    }

    [Fact]
    public async Task AddUserToRole_FallbackRefusedByRest_IsThrown()
    {
        AnswerMethodCallsWithValidationErrors("SystemIdentityUserAddRoleMethodInput", "addRole");
#pragma warning disable CS0618 // the obsolete REST method is the expected fallback
        A.CallTo(() => _identity.AddRoleToUser(A<string>._, A<string>._))
            .ThrowsAsync(new ServiceClientResultException("User 'john.doe' already has role 'dataanalyst'",
                System.Net.HttpStatusCode.BadRequest));
#pragma warning restore CS0618
        var logger = new RecordingLogger<AddUserToRole>();

        await Assert.ThrowsAsync<ServiceClientResultException>(() =>
            Add(logger, "-un", "john.doe", "-r", "DataAnalyst").Execute());

        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("updated"));
    }

    [Fact]
    public async Task RemoveUserFromRole_RunsTheRemoveRoleMethod_ByRtId_WithUnchangedOutput()
    {
        var logger = new RecordingLogger<RemoveUserFromRole>();
        var command = Remove(logger, "-un", "john.doe", "-r", "DataAnalyst", "-y");

        await command.Execute();

        var request = Assert.Single(_requests);
        Assert.Contains("removeRole", request.Query, StringComparison.Ordinal);
        Assert.Contains(UserRtId, Variables(request));
        A.CallTo(() => _confirmation.Confirm(A<string>._)).MustNotHaveHappened();
        AssertNoRestRoleCall();
        Assert.Collection(logger.Entries,
            e => Assert.StartsWith("Removing user 'john.doe' from role 'dataanalyst' at ", e.Message),
            e => Assert.Equal("User 'john.doe' updated", e.Message));
    }

    [Fact]
    public async Task RemoveUserFromRole_DeclaredError_PrintsCodeAndTheRestMessage()
    {
        AnswerMethodCalls("ROLE_NOT_ASSIGNED");
        var command = Remove(new RecordingLogger<RemoveUserFromRole>(), "-un", "john.doe", "-r", "DataAnalyst", "-y");

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() => command.Execute());

        Assert.Equal("ROLE_NOT_ASSIGNED: User 'john.doe' doesn't have role 'dataanalyst'", ex.Message);
    }

    [Fact]
    public async Task RemoveUserFromRole_RoleNotFound_PrintsCodeMessageAndParameter()
    {
        AnswerMethodCalls("ROLE_NOT_FOUND", new Dictionary<string, object?> { ["roleName"] = "dataanalyst" });
        var command = Remove(new RecordingLogger<RemoveUserFromRole>(), "-un", "john.doe", "-r", "DataAnalyst", "-y");

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() => command.Execute());

        Assert.Equal("ROLE_NOT_FOUND: Role with name 'dataanalyst' not found. (roleName=dataanalyst)", ex.Message);
    }

    [Fact]
    public async Task RemoveUserFromRole_Declined_CallsNothing()
    {
        A.CallTo(() => _confirmation.Confirm(A<string>._)).Returns(false);
        var command = Remove(new RecordingLogger<RemoveUserFromRole>(), "-un", "john.doe", "-r", "DataAnalyst");

        await Assert.ThrowsAsync<ToolException>(() => command.Execute());

        Assert.Empty(_requests);
    }

    [Fact]
    public async Task UndeclaredRefusal_IsThrown_AndFormattedWithCodeAndReason()
    {
        AnswerMethodCalls("METHOD_FORBIDDEN", new Dictionary<string, object?> { ["reason"] = "self-grant" });
        var command = Add(new RecordingLogger<AddUserToRole>(), "-un", "john.doe", "-r", "DataAnalyst");

        var ex = await Assert.ThrowsAsync<CkMethodCallException>(() => command.Execute());

        Assert.Equal("METHOD_FORBIDDEN", ex.Code);
        AssertNoRestRoleCall(); // AB#8838: a refusal is no reason for the REST fallback
        Assert.Equal("The CK method call 'System.Identity/User.AddRole-1' failed: METHOD_FORBIDDEN (reason=self-grant)",
            CkMethodErrorText.Format(ex));
    }

    [Fact]
    public async Task UnknownUser_StopsBeforeTheMethodCall()
    {
        A.CallTo(() => _identity.GetUserByNameEmailOrId(A<string>._)).Returns(new UserDto { Name = "ghost" });
        var command = Add(new RecordingLogger<AddUserToRole>(), "-un", "ghost", "-r", "DataAnalyst");

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() => command.Execute());

        Assert.Equal("User with name 'ghost' not found.", ex.Message);
        Assert.Empty(_requests);
    }

    private AddUserToRole Add(RecordingLogger<AddUserToRole> logger, params string[] args)
    {
        var command = new AddUserToRole(logger, Options(), _identity, _tenant, _auth);
        command.CommandArgumentValue.ParseLayer(args);
        return command;
    }

    private RemoveUserFromRole Remove(RecordingLogger<RemoveUserFromRole> logger, params string[] args)
    {
        var command = new RemoveUserFromRole(logger, Options(), _identity, _tenant, _auth, _confirmation);
        command.CommandArgumentValue.ParseLayer(args);
        return command;
    }

    private static IOptions<OctoToolOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new OctoToolOptions { TenantId = "customer" });

    private void AssertNoRestRoleCall() =>
        A.CallTo(_identity)
            .Where(c => c.Method.Name == "AddRoleToUser" || c.Method.Name == "RemoveRoleFromUser")
            .MustNotHaveHappened();

    private static string Variables(GraphQLRequest request) =>
        System.Text.Json.JsonSerializer.Serialize(request.Variables);

    /// <summary>
    ///     Answers every <c>SendMutationAsync</c> with the GraphQL validation errors of a server whose schema lacks the method
    ///     (asset repository before 3.5.43 / identity before 3.5.52): the shape the FH lab installation (3.5.23) answered.
    /// </summary>
    private void AnswerMethodCallsWithValidationErrors(string inputType, string field)
    {
        A.CallTo(_tenant).Where(c => c.Method.Name == nameof(ITenantClient.SendMutationAsync))
            .WithNonVoidReturnType()
            .ReturnsLazily(call =>
            {
                _requests.Add((GraphQLRequest)call.Arguments[0]!);
                var responseType = call.Method.ReturnType.GetGenericArguments()[0];
                GraphQLError Validation(string message, string code, string number) => new()
                {
                    Message = message,
                    Extensions = new Map { ["code"] = code, ["codes"] = new List<object> { code }, ["number"] = number }
                };
                var error = new QlQueryErrorException("GraphQL error",
                [
                    Validation($"Variable 'input' cannot be non-input type '{inputType}'.", "VARIABLES_ARE_INPUT_TYPES", "5.8.2"),
                    Validation($"Unknown type {inputType}.", "KNOWN_TYPE_NAMES", "5.5.1.2"),
                    Validation($"Cannot query field '{field}' on type 'SystemIdentityUserMutations'.", "FIELDS_ON_CORRECT_TYPE", "5.3.1")
                ]);
                return typeof(Task).GetMethod(nameof(Task.FromException), 1, [typeof(Exception)])!
                    .MakeGenericMethod(responseType).Invoke(null, [error])!;
            });
    }

    /// <summary>
    ///     Answers every <c>SendMutationAsync</c> of the generated client: success (a response with an invocation id) or the
    ///     GraphQL error the method gateway sends for <paramref name="errorCode" /> (extensions <c>code</c>, <c>parameters</c>).
    /// </summary>
    private void AnswerMethodCalls(string? errorCode, Dictionary<string, object?>? parameters = null)
    {
        A.CallTo(_tenant).Where(c => c.Method.Name == nameof(ITenantClient.SendMutationAsync))
            .WithNonVoidReturnType()
            .ReturnsLazily(call =>
            {
                _requests.Add((GraphQLRequest)call.Arguments[0]!);
                var responseType = call.Method.ReturnType.GetGenericArguments()[0];
                if (errorCode != null)
                {
                    var extensions = new Map
                    {
                        ["code"] = errorCode,
                        ["parameters"] = new Dictionary<string, object?>(parameters ?? [])
                        {
                            ["methodId"] = "System.Identity/User.Role-1"
                        }
                    };
                    var error = new QlQueryErrorException("GraphQL error",
                        [new GraphQLError { Message = $"The CK method call failed: {errorCode}", Extensions = extensions }]);
                    return typeof(Task).GetMethod(nameof(Task.FromException), 1, [typeof(Exception)])!
                        .MakeGenericMethod(responseType).Invoke(null, [error])!;
                }

                var response = Activator.CreateInstance(responseType)!;
                responseType.GetProperty("InvocationId", BindingFlags.Public | BindingFlags.Instance)?
                    .SetValue(response, "inv-1");
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(responseType)
                    .Invoke(null, [response])!;
            });
    }
}
