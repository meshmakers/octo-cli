using System.Collections.Concurrent;
using System.Net;
using System.Text;
using FakeItEasy;
using GraphQL;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace ManagementTool.Tests;

/// <summary>
///     AB#9872: <c>GetClientRoles</c>, <c>AddClientToRole</c> and <c>RemoveClientFromRole</c> with
///     <c>--child-tenant-id</c> act in a child tenant of the context's tenant with the caller's token for that child
///     (<see cref="IChildTenantAccess" />, the path of <c>Delete --disable-features</c>). The context's token never reaches
///     the child's routes; an unknown tenant, a tenant that is no child and a caller without a token or right for the
///     child end with a clear message and a non-zero exit code, without output. <c>GetClientRoles</c> runs the real
///     <see cref="ChildTenantIdentityFactory" /> and <see cref="IdentityServicesClient" /> against a loopback stand-in
///     of the identity service that records the bearer token of every request; the real <see cref="Runner" /> maps the
///     result to the exit code.
/// </summary>
public sealed class ClientRolesChildTenantCommandTests : IClassFixture<ClientRolesChildTenantCommandTests.IdentityStub>
{
    private const string Parent = "parent";
    private const string Child = "child-a";
    private const string Sibling = "sibling-b";
    private const string ClientId = "ci-deploy-test-2";
    private const string ClientRtId = "65f0c0ffee00000000000004";
    private const string CommunicationManagementId = "6650c0ffee00000000000001";
    private const string ChildToken = "child-token-for-tests";
    private const string ContextToken = "context-token-for-tests";

    private readonly IdentityStub _stub;
    private readonly List<string> _output = [];
    private readonly IAssetServicesClient _asset = A.Fake<IAssetServicesClient>();
    private readonly IChildTenantAccess _access = A.Fake<IChildTenantAccess>();
    private readonly IIdentityServicesClient _contextIdentity = A.Fake<IIdentityServicesClient>();
    private readonly ITenantClient _contextTenant = A.Fake<ITenantClient>();
    private readonly IAuthenticationService _contextAuth = A.Fake<IAuthenticationService>();

    public ClientRolesChildTenantCommandTests(IdentityStub stub)
    {
        _stub = stub;
        _stub.Reset();
        foreach (var tenant in new[] { Child, Sibling })
        {
            _stub.Tenants[tenant] = new TenantState
            {
                RolesJson = $$"""[{"id":"{{CommunicationManagementId}}","name":"CommunicationManagement"}]""",
                ClientRoles = { [ClientId] = $"""["{CommunicationManagementId}"]""" }
            };
        }

        A.CallTo(() => _asset.ServiceUri).Returns(new Uri("https://assets.example.test/parent/"));
        A.CallTo(() => _asset.GetTenantDescendantsAsync()).Returns(Task.FromResult<IEnumerable<TenantDto>>(
        [
            new TenantDto { TenantId = Child, Database = Child },
            new TenantDto { TenantId = "grandchild-c", Database = "grandchild-c" }
        ]));
        A.CallTo(() => _access.GetAccessTokenAsync(A<string>._)).Returns(Task.FromResult(ChildToken));
        A.CallTo(() => _contextAuth.EnsureAuthenticated(A<IServiceClientAccessToken>._))
            .Invokes((IServiceClientAccessToken t) => t.AccessToken = ContextToken);
    }

    [Fact]
    public async Task GetClientRoles_ChildTenant_ReadsTheChildWithTheChildToken_PrintsTheSameJson_AndExitsWithZero()
    {
        var (exitCode, errors, logs) = await RunGetClientRoles("-id", ClientId, "-ctid", Child);

        Assert.Equal(0, exitCode);
        Assert.Empty(errors);
        var role = Assert.Single(JArray.Parse(Assert.Single(_output)));
        Assert.Equal("CommunicationManagement", (string?)role["Name"]);
        Assert.Equal(CommunicationManagementId, (string?)role["Id"]);
        Assert.Equal([$"GET /{Child}/v1/roles", $"GET /{Child}/v1/clients/{ClientId}/roles"],
            _stub.Requests.Select(r => r.Request));
        Assert.All(_stub.Requests,
            r => Assert.Equal($"Bearer {ChildToken}", r.Authorization, ignoreCase: true));
        A.CallTo(() => _access.GetAccessTokenAsync(Child)).MustHaveHappenedOnceExactly();
        A.CallTo(_contextIdentity).Where(c => c.Method.Name != "get_ServiceUri").MustNotHaveHappened();
        Assert.DoesNotContain(logs, l => l.Message.Contains(ChildToken, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetClientRoles_ChildTenantIdInUpperCase_IsNormalized()
    {
        var (exitCode, _, _) = await RunGetClientRoles("-id", ClientId, "--child-tenant-id", "CHILD-A");

        Assert.Equal(0, exitCode);
        A.CallTo(() => _access.GetAccessTokenAsync(Child)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task GetClientRoles_ChildAtAnyDepth_IsAccepted()
    {
        _stub.Tenants["grandchild-c"] = _stub.Tenants[Child];

        var (exitCode, _, _) = await RunGetClientRoles("-id", ClientId, "-ctid", "grandchild-c");

        Assert.Equal(0, exitCode);
        Assert.All(_stub.Requests, r => Assert.StartsWith("GET /grandchild-c/", r.Request));
    }

    [Fact]
    public async Task GetClientRoles_UnknownChild_NamesTenantAndParent_NoTokenRequested_AndExitsNonZero()
    {
        var (exitCode, errors, _) = await RunGetClientRoles("-id", ClientId, "-ctid", "no-such-tenant");

        Assert.Equal(-5, exitCode);
        Assert.Equal(
            "Tenant 'no-such-tenant' is not a child tenant of 'parent' at 'https://assets.example.test': it does not " +
            "exist or belongs to another parent. GetTenants -r lists the child tenants of the context.",
            Assert.Single(errors).Message);
        AssertNothingReachedTheChild();
    }

    [Fact]
    public async Task GetClientRoles_TenantThatIsNotAChild_IsRefused_EvenWhenItExists()
    {
        var (exitCode, errors, _) = await RunGetClientRoles("-id", ClientId, "-ctid", Sibling);

        Assert.Equal(-5, exitCode);
        Assert.StartsWith($"Tenant '{Sibling}' is not a child tenant of 'parent'", Assert.Single(errors).Message);
        AssertNothingReachedTheChild();
    }

    [Fact]
    public async Task GetClientRoles_TheContextsOwnTenant_IsNotAChild()
    {
        var (exitCode, errors, _) = await RunGetClientRoles("-id", ClientId, "-ctid", Parent);

        Assert.Equal(-5, exitCode);
        Assert.Equal("Tenant 'parent' is the tenant of the context, not a child of it: omit --child-tenant-id.",
            Assert.Single(errors).Message);
        A.CallTo(() => _asset.GetTenantDescendantsAsync()).MustNotHaveHappened();
        AssertNothingReachedTheChild();
    }

    [Fact]
    public async Task GetClientRoles_CallerWithoutTokenForTheChild_GetsTheReason_AndExitsNonZero()
    {
        A.CallTo(() => _access.GetAccessTokenAsync(Child)).ThrowsAsync(new ToolException(
            $"No token for tenant '{Child}' could be obtained from the signed-in user: not an administrator there."));

        var (exitCode, errors, _) = await RunGetClientRoles("-id", ClientId, "-ctid", Child);

        Assert.Equal(-5, exitCode);
        Assert.StartsWith($"No token for tenant '{Child}'", Assert.Single(errors).Message);
        Assert.Empty(_stub.Requests);
        Assert.Empty(_output);
    }

    [Fact]
    public async Task GetClientRoles_CallerWithoutRightInTheChild_Gets403_AndExitsNonZero_WithoutOutput()
    {
        _stub.Tenants[Child].Forbidden = true;

        var (exitCode, errors, logs) = await RunGetClientRoles("-id", ClientId, "-ctid", Child);

        Assert.Equal(-3, exitCode);
        Assert.NotEmpty(errors);
        Assert.Empty(_output);
        Assert.DoesNotContain(logs, l => l.Message.Contains(ChildToken, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetClientRoles_CallerWhoMayNotListTheChildren_IsRefused_BeforeAnyToken()
    {
        A.CallTo(() => _asset.GetTenantDescendantsAsync())
            .ThrowsAsync(new ServiceClientResultException(HttpStatusCode.Forbidden));

        var (exitCode, errors, _) = await RunGetClientRoles("-id", ClientId, "-ctid", Child);

        Assert.Equal(-3, exitCode);
        Assert.NotEmpty(errors);
        AssertNothingReachedTheChild();
    }

    [Fact]
    public async Task GetClientRoles_WithoutTheOption_ReadsTheContextsTenant_AndNeverTheChild()
    {
        A.CallTo(() => _contextIdentity.GetRoles()).Returns(Task.FromResult<IEnumerable<RoleDto>>([]));
        A.CallTo(() => _contextIdentity.GetClientDirectRoles(ClientId))
            .Returns(Task.FromResult<IEnumerable<string>>([]));

        var (exitCode, _, _) = await RunGetClientRoles("-id", ClientId);

        Assert.Equal(0, exitCode);
        Assert.Equal("[]", Assert.Single(_output));
        A.CallTo(() => _contextIdentity.GetClientDirectRoles(ClientId)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _asset.GetTenantDescendantsAsync()).MustNotHaveHappened();
        A.CallTo(() => _access.GetAccessTokenAsync(A<string>._)).MustNotHaveHappened();
        Assert.Empty(_stub.Requests);
    }

    [Fact]
    public async Task AddClientToRole_ChildTenant_CallsTheMethodInTheChild_WithTheChildToken()
    {
        var child = NewChildClients();

        var exitCode = await RunMethodCommand(typeof(AddClientToRole), child.Factory,
            "-id", ClientId, "-r", "CommunicationManagement", "-ctid", Child);

        Assert.Equal(0, exitCode);
        var request = Assert.Single(child.Requests);
        Assert.Contains("addRole(", request.Query);
        Assert.Contains(ClientRtId, Variables(request));
        Assert.Equal(ChildToken, child.Tenant.AccessToken.AccessToken);
        A.CallTo(() => child.Identity.GetClient(ClientId)).MustHaveHappenedOnceExactly();
        A.CallTo(_contextTenant).Where(c => c.Method.Name == nameof(ITenantClient.SendMutationAsync))
            .MustNotHaveHappened();
        A.CallTo(() => _contextIdentity.GetClient(A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task RemoveClientFromRole_ChildTenant_CallsTheMethodInTheChild_WithTheChildToken()
    {
        var child = NewChildClients();

        var exitCode = await RunMethodCommand(typeof(RemoveClientFromRole), child.Factory,
            "-id", ClientId, "-r", "CommunicationManagement", "-ctid", Child, "-y");

        Assert.Equal(0, exitCode);
        Assert.Contains("removeRole(", Assert.Single(child.Requests).Query);
        Assert.Equal(ChildToken, child.Tenant.AccessToken.AccessToken);
        A.CallTo(_contextTenant).Where(c => c.Method.Name == nameof(ITenantClient.SendMutationAsync))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task RemoveClientFromRole_ChildTenant_AsksWithTheTenantName_AndStopsOnNo()
    {
        var child = NewChildClients();
        var confirmation = A.Fake<IConfirmationService>();
        A.CallTo(() => confirmation.Confirm(A<string>._)).Returns(false);

        var exitCode = await RunMethodCommand(typeof(RemoveClientFromRole), child.Factory,
            confirmation, "-id", ClientId, "-r", "CommunicationManagement", "-ctid", Child);

        Assert.NotEqual(0, exitCode);
        A.CallTo(() => confirmation.Confirm(
                $"Are you sure you want to remove client '{ClientId}' from role 'CommunicationManagement' in tenant '{Child}'?"))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => child.Factory.CreateAsync(A<string>._)).MustNotHaveHappened();
        Assert.Empty(child.Requests);
    }

    [Theory]
    [InlineData(typeof(AddClientToRole))]
    [InlineData(typeof(RemoveClientFromRole))]
    public async Task WriteCommands_ChildThatIsNoChild_ChangeNothing_AndExitNonZero(Type command)
    {
        var child = NewChildClients();
        A.CallTo(() => child.Factory.CreateAsync(Sibling)).ThrowsAsync(
            new ToolException($"Tenant '{Sibling}' is not a child tenant of 'parent'."));

        var exitCode = await RunMethodCommand(command, child.Factory,
            "-id", ClientId, "-r", "CommunicationManagement", "-ctid", Sibling, "-y");

        Assert.Equal(-5, exitCode);
        Assert.Empty(child.Requests);
        A.CallTo(_contextTenant).Where(c => c.Method.Name == nameof(ITenantClient.SendMutationAsync))
            .MustNotHaveHappened();
    }

    [Theory]
    [InlineData(typeof(AddClientToRole))]
    [InlineData(typeof(RemoveClientFromRole))]
    public async Task WriteCommands_CallerWithoutRightInTheChild_EndWithTheMethodRefusal_AndExitNonZero(Type command)
    {
        var child = NewChildClients(errorCode: "METHOD_FORBIDDEN");

        var exitCode = await RunMethodCommand(command, child.Factory,
            "-id", ClientId, "-r", "CommunicationManagement", "-ctid", Child, "-y");

        Assert.Equal(-3, exitCode);
        Assert.Single(child.Requests);
    }

    [Fact]
    public async Task FixedTokenAuthentication_HandsOnlyTheChildToken()
    {
        var auth = new ChildTenantIdentityFactory.FixedTokenAuthentication(ChildToken);
        var token = new ServiceClientAccessToken { AccessToken = ContextToken };

        await auth.EnsureAuthenticated(token);

        Assert.Equal(ChildToken, token.AccessToken);
        Assert.Throws<InvalidOperationException>(() => auth.SaveAuthenticationData(new()));
    }

    private void AssertNothingReachedTheChild()
    {
        Assert.Empty(_stub.Requests);
        Assert.Empty(_output);
        A.CallTo(() => _access.GetAccessTokenAsync(A<string>._)).MustNotHaveHappened();
    }

    private async Task<(int ExitCode, List<LogEntry> Errors, List<LogEntry> Logs)> RunGetClientRoles(
        params string[] args)
    {
        var options = Options.Create(new OctoToolOptions
        {
            TenantId = Parent, IdentityServiceUrl = _stub.BaseUri, AssetServiceUrl = _stub.BaseUri
        });
        var factory = new ChildTenantIdentityFactory(options, _asset, _contextAuth, _access);
        var console = A.Fake<IConsoleService>();
        A.CallTo(() => console.WriteLine(A<string>._)).Invokes((string s) => _output.Add(s));
        var commandLogger = new RecordingLogger<GetClientRoles>();
        var command = new GetClientRoles(commandLogger, options, console, _contextIdentity, _contextAuth, factory);
        command.CommandArgumentValue.ParseLayer(args);

        var (exitCode, runnerLogger) = await RunThroughRunner(command.Execute);
        return (exitCode, runnerLogger.Entries.Where(e => e.Level == LogLevel.Error).ToList(),
            [.. commandLogger.Entries, .. runnerLogger.Entries]);
    }

    private Task<int> RunMethodCommand(Type type, IChildTenantIdentityFactory factory, params string[] args) =>
        RunMethodCommand(type, factory, A.Fake<IConfirmationService>(), args);

    private async Task<int> RunMethodCommand(Type type, IChildTenantIdentityFactory factory,
        IConfirmationService confirmation, params string[] args)
    {
        var options = Options.Create(new OctoToolOptions { TenantId = Parent });
        Func<Task> execute;
        if (type == typeof(AddClientToRole))
        {
            var command = new AddClientToRole(new RecordingLogger<AddClientToRole>(), options, _contextIdentity,
                _contextTenant, _contextAuth, factory);
            command.CommandArgumentValue.ParseLayer(args);
            execute = command.Execute;
        }
        else
        {
            var command = new RemoveClientFromRole(new RecordingLogger<RemoveClientFromRole>(), options,
                _contextIdentity, _contextTenant, _contextAuth, confirmation, factory);
            command.CommandArgumentValue.ParseLayer(args);
            execute = command.Execute;
        }

        return (await RunThroughRunner(execute)).ExitCode;
    }

    private static async Task<(int ExitCode, RecordingLogger<Runner> Logger)> RunThroughRunner(Func<Task> execute)
    {
        var parser = A.Fake<ICommandParser>();
        A.CallTo(() => parser.ParseAndValidateAsync(A<string?>._))
            .ReturnsLazily(() => Task.Run(execute, TestContext.Current.CancellationToken));
        var contexts = A.Fake<IContextManager>();
        A.CallTo(() => contexts.GetEffectiveContextName()).Returns("test");
        var logger = new RecordingLogger<Runner>();
        var exitCode = await new Runner(logger, parser, contexts, ContextSelection.None).DoActionAsync();
        return (exitCode, logger);
    }

    private static ChildClients NewChildClients(string? errorCode = null)
    {
        var identity = A.Fake<IIdentityServicesClient>();
        A.CallTo(() => identity.ServiceUri).Returns(new Uri($"https://identity.example.test/{Child}/v1/"));
        A.CallTo(() => identity.GetClient(A<string>._)).ReturnsLazily((string id) =>
            Task.FromResult(new ClientDto { ClientId = id, RtId = ClientRtId }));
        var tenant = A.Fake<ITenantClient>();
        A.CallTo(() => tenant.AccessToken).Returns(new ServiceClientAccessToken());
        var requests = new List<GraphQLRequest>();
        MethodFakes.AnswerMethodCalls(tenant, requests, errorCode);
        var factory = A.Fake<IChildTenantIdentityFactory>();
        A.CallTo(() => factory.CreateAsync(Child)).Returns(Task.FromResult(new ChildTenantIdentity(Child, identity,
            tenant, new ChildTenantIdentityFactory.FixedTokenAuthentication(ChildToken))));
        return new ChildClients(factory, identity, tenant, requests);
    }

    private static string Variables(GraphQLRequest request) =>
        System.Text.Json.JsonSerializer.Serialize(request.Variables);

    private sealed record ChildClients(
        IChildTenantIdentityFactory Factory,
        IIdentityServicesClient Identity,
        ITenantClient Tenant,
        List<GraphQLRequest> Requests);

    public sealed class TenantState
    {
        public string RolesJson { get; init; } = "[]";
        public Dictionary<string, string> ClientRoles { get; } = new();
        public bool Forbidden { get; set; }
    }

    public sealed record RecordedRequest(string Request, string? Authorization);

    /// <summary>
    ///     Loopback stand-in for the identity service's role reads that records the bearer token of every request (one
    ///     listener per class: slow to start on macOS).
    /// </summary>
    public sealed class IdentityStub : IDisposable
    {
        private readonly HttpListener _listener = new();

        public IdentityStub()
        {
            var firstPort = 47000 + Random.Shared.Next(0, 2000);
            for (var port = firstPort;; port++)
            {
                try
                {
                    BaseUri = $"http://127.0.0.1:{port}/";
                    _listener.Prefixes.Clear();
                    _listener.Prefixes.Add(BaseUri);
                    _listener.Start();
                    break;
                }
                catch (HttpListenerException) when (port < firstPort + 100)
                {
                    // port taken, try the next one (bounded)
                }
            }

            _ = Task.Run(Serve);
        }

        public string BaseUri { get; private set; } = "";
        public ConcurrentDictionary<string, TenantState> Tenants { get; } = new();
        public ConcurrentQueue<RecordedRequest> Requests { get; private set; } = new();

        public void Reset()
        {
            Tenants.Clear();
            Requests = new ConcurrentQueue<RecordedRequest>();
        }

        public void Dispose() => _listener.Close();

        private async Task Serve()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception)
                {
                    return;
                }

                var path = context.Request.Url!.AbsolutePath;
                Requests.Enqueue(new RecordedRequest($"{context.Request.HttpMethod} {path}",
                    context.Request.Headers["Authorization"]));
                var (status, body) = Answer(path.Trim('/').Split('/'));
                context.Response.StatusCode = (int)status;
                if (body != null)
                {
                    var bytes = Encoding.UTF8.GetBytes(body);
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes);
                }

                context.Response.Close();
            }
        }

        private (HttpStatusCode, string?) Answer(string[] segments)
        {
            if (segments.Length < 3 || !Tenants.TryGetValue(segments[0], out var tenant))
            {
                return (HttpStatusCode.NotFound, null);
            }

            if (tenant.Forbidden)
            {
                return (HttpStatusCode.Forbidden, null);
            }

            if (segments is [_, "v1", "roles"])
            {
                return (HttpStatusCode.OK, tenant.RolesJson);
            }

            if (segments is [_, "v1", "clients", var id, "roles"])
            {
                return tenant.ClientRoles.TryGetValue(Uri.UnescapeDataString(id), out var roles)
                    ? (HttpStatusCode.OK, roles)
                    : (HttpStatusCode.NotFound, $$"""{"message":"Client with id '{{id}}' not found."}""");
            }

            return (HttpStatusCode.NotFound, null);
        }
    }
}
