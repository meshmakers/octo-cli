using System.Collections.Concurrent;
using System.Net;
using System.Text;
using FakeItEasy;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace ManagementTool.Tests;

/// <summary>
///     AB#9699: <c>GetClientRoles</c> lists the direct roles of a client in the context's tenant as id and name, and an
///     unknown client or tenant ends with a clear message and a non-zero exit code. The real
///     <see cref="IdentityServicesClient" /> talks to a local HTTP listener that answers like the identity service
///     (<c>GET {tenant}/v1/roles</c>, <c>GET {tenant}/v1/clients/{id}/roles</c>; an unknown tenant is a 404 without a
///     body from the tenant middleware, an unknown client a 404 with a message); the real <see cref="Runner" /> maps the
///     result to the exit code.
/// </summary>
public sealed class GetClientRolesCommandTests : IClassFixture<GetClientRolesCommandTests.IdentityStub>
{
    private const string Tenant = "customer";
    private const string ClientId = "ci-deploy-test-2";
    private const string CommunicationManagementId = "6650c0ffee00000000000001";
    private const string TenantManagementId = "6650c0ffee00000000000002";

    private readonly IdentityStub _stub;
    private readonly List<string> _output = [];

    public GetClientRolesCommandTests(IdentityStub stub)
    {
        _stub = stub;
        _stub.Reset();
        _stub.Tenants[Tenant] = new TenantState
        {
            RolesJson =
                $$"""[{"id":"{{TenantManagementId}}","name":"TenantManagement"},{"id":"{{CommunicationManagementId}}","name":"CommunicationManagement"},{"id":"6650c0ffee00000000000003","name":"UserManagement"}]""",
            ClientRoles = { [ClientId] = $"""["{TenantManagementId}","{CommunicationManagementId}"]""" }
        };
    }

    [Fact]
    public async Task KnownClient_PrintsItsDirectRolesWithIdAndName_SortedByName_AndExitsWithZero()
    {
        var (exitCode, errors) = await Run(Tenant, ClientId);

        Assert.Equal(0, exitCode);
        Assert.Empty(errors);
        var printed = JArray.Parse(Assert.Single(_output));
        Assert.Equal(["CommunicationManagement", "TenantManagement"], printed.Select(r => (string?)r["Name"]));
        Assert.Equal([CommunicationManagementId, TenantManagementId], printed.Select(r => (string?)r["Id"]));
        Assert.Contains($"GET /{Tenant}/v1/clients/{ClientId}/roles", _stub.Requests);
    }

    [Fact]
    public async Task ClientWithoutDirectRoles_PrintsAnEmptyList_AndExitsWithZero()
    {
        _stub.Tenants[Tenant].ClientRoles["no-roles"] = "[]";

        var (exitCode, errors) = await Run(Tenant, "no-roles");

        Assert.Equal(0, exitCode);
        Assert.Empty(errors);
        Assert.Empty(JArray.Parse(Assert.Single(_output)));
    }

    [Fact]
    public async Task RoleIdNotInTheTenant_IsPrintedWithoutName_AndWarned()
    {
        _stub.Tenants[Tenant].ClientRoles["stale"] = """["6650c0ffee00000000000099"]""";

        var (exitCode, _) = await Run(Tenant, "stale");

        Assert.Equal(0, exitCode);
        var role = Assert.Single(JArray.Parse(Assert.Single(_output)));
        Assert.Equal("6650c0ffee00000000000099", (string?)role["Id"]);
        Assert.Equal(JTokenType.Null, role["Name"]!.Type);
    }

    [Fact]
    public async Task UnknownClient_NamesClientAndTenant_PrintsNothing_AndExitsNonZero()
    {
        var (exitCode, errors) = await Run(Tenant, "missing-client");

        Assert.Equal(-5, exitCode);
        var error = Assert.Single(errors);
        Assert.Equal($"Client 'missing-client' was not found in tenant '{Tenant}'.", error.Message);
        Assert.Empty(_output);
    }

    [Fact]
    public async Task UnknownTenant_NamesTheTenant_NotTheClient_PrintsNothing_AndExitsNonZero()
    {
        var (exitCode, errors) = await Run("no-such-tenant", ClientId);

        Assert.Equal(-5, exitCode);
        var error = Assert.Single(errors);
        Assert.StartsWith("Tenant 'no-such-tenant' was not found at 'http://127.0.0.1:", error.Message);
        Assert.Empty(_output);
        Assert.DoesNotContain(_stub.Requests, r => r.Contains("/clients/", StringComparison.Ordinal));
    }

    private async Task<(int ExitCode, List<LogEntry> Errors)> Run(string tenant, string clientId)
    {
        var client = new IdentityServicesClient(
            new IdentityServiceClientOptions { EndpointUri = _stub.BaseUri, TenantId = tenant },
            A.Fake<IIdentityServiceClientAccessToken>());
        var console = A.Fake<IConsoleService>();
        A.CallTo(() => console.WriteLine(A<string>._)).Invokes((string s) => _output.Add(s));
        var command = new GetClientRoles(new RecordingLogger<GetClientRoles>(),
            Options.Create(new OctoToolOptions { TenantId = tenant }), console, client,
            A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(["-id", clientId]);

        var parser = A.Fake<ICommandParser>();
        A.CallTo(() => parser.ParseAndValidateAsync(A<string?>._))
            .Returns(Task.Run(command.Execute, TestContext.Current.CancellationToken));
        var contexts = A.Fake<IContextManager>();
        A.CallTo(() => contexts.GetEffectiveContextName()).Returns("test");
        var logger = new RecordingLogger<Runner>();

        var exitCode = await new Runner(logger, parser, contexts, ContextSelection.None).DoActionAsync();
        return (exitCode, logger.Entries.Where(e => e.Level == LogLevel.Error).ToList());
    }

    public sealed class TenantState
    {
        public string RolesJson { get; init; } = "[]";
        public Dictionary<string, string> ClientRoles { get; } = new();
    }

    /// <summary>Loopback stand-in for the identity service's role reads (one listener per class: slow to start on macOS).</summary>
    public sealed class IdentityStub : IDisposable
    {
        private readonly HttpListener _listener = new();

        public IdentityStub()
        {
            var firstPort = 45000 + Random.Shared.Next(0, 2000);
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
        public ConcurrentQueue<string> Requests { get; private set; } = new();

        public void Reset()
        {
            Tenants.Clear();
            Requests = new ConcurrentQueue<string>();
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
                Requests.Enqueue($"{context.Request.HttpMethod} {path}");
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
            // {tenant}/v1/roles | {tenant}/v1/clients/{id}/roles
            if (segments.Length < 3 || !Tenants.TryGetValue(segments[0], out var tenant))
            {
                return (HttpStatusCode.NotFound, null); // TenantMiddleware: unknown tenant, no body
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
