using System.Net;
using System.Text;
using System.Text.Json;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;

namespace ManagementTool.Tests;

/// <summary>Builds unsigned JWT-shaped strings; octo-cli and the SDK only read the payload.</summary>
internal static class TestJwt
{
    public static string For(string? tenantId, string marker = "x")
    {
        var payload = new Dictionary<string, object> { ["sub"] = "user-1", ["marker"] = marker };
        if (tenantId != null)
        {
            payload["tenant_id"] = tenantId;
        }

        return $"{Encode("""{"alg":"none"}""")}.{Encode(JsonSerializer.Serialize(payload))}.sig";
    }

    private static string Encode(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Counts exchanges and issues a token for the requested tenant, or denies.</summary>
internal sealed class CountingExchange : ITokenExchangeClient
{
    public int Calls { get; private set; }
    public List<string> Targets { get; } = [];
    public TenantTokenDenialCause? Deny { get; set; }

    public Task<AuthenticationData> ExchangeTokenForTenantAsync(string subjectToken, string targetTenantId,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        Targets.Add(targetTenantId);
        if (Deny is { } cause)
        {
            throw new TenantTokenExchangeDeniedException(targetTenantId, cause, "denied");
        }

        return Task.FromResult(new AuthenticationData
        {
            AccessToken = TestJwt.For(targetTenantId, $"exchanged{Calls}"),
            ExpiresAt = DateTime.Now.AddHours(1)
        });
    }
}

/// <summary>A context manager with one effective context; counts saves.</summary>
internal sealed class StubContextManager(ContextEntry? entry, string? name = "child-ctx") : IContextManager
{
    public int Saves { get; private set; }

    public string? GetEffectiveContextName() => name;
    public ContextEntry? GetEffectiveContext() => entry;
    public void SaveEffectiveContext() => Saves++;

    public string ConfigurationFilePath => "<test>";
    public bool IsContextOverridden => false;
    public ContextConfiguration Load() => new();
    public ContextEntry? GetActiveContext() => entry;
    public string? GetActiveContextName() => name;
    public void SelectContext(string n) => throw new NotImplementedException();
    public void AddOrUpdateContext(string n, ContextEntry e) => throw new NotImplementedException();
    public void RemoveContext(string n) => throw new NotImplementedException();
    public void SetActiveContext(string n) => throw new NotImplementedException();
    public IReadOnlyDictionary<string, ContextEntry> ListContexts() => throw new NotImplementedException();
    public void MigrateIfNeeded() => throw new NotImplementedException();
}

/// <summary>An in-process HTTP endpoint that records method, path, query and the Authorization header.</summary>
internal sealed class WireService : IDisposable
{
    private readonly HttpListener _listener = new();

    public WireService()
    {
        using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        BaseUrl = $"http://127.0.0.1:{((IPEndPoint)socket.LocalEndPoint!).Port}/";
        socket.Close();
        _listener.Prefixes.Add(BaseUrl);
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    public string BaseUrl { get; }
    public int Status { get; set; } = 200;
    public string Body { get; set; } = """{"items":[],"modelsNeedingActionCount":0}""";
    public List<string> Requests { get; } = [];
    public List<string> Authorization { get; } = [];

    public void Dispose() => _listener.Abort();

    private async Task ServeAsync()
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

            lock (Requests)
            {
                Requests.Add($"{context.Request.HttpMethod} {context.Request.Url?.PathAndQuery}");
                var header = context.Request.Headers["Authorization"];
                if (header != null)
                {
                    Authorization.Add(header);
                }
            }

            var bytes = Encoding.UTF8.GetBytes(Body);
            context.Response.StatusCode = Status;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }
}
