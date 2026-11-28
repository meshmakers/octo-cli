using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ManagementTool.Tests;

/// <summary>A local GraphQL endpoint answering with queued bodies in order (then <c>{}</c>) and recording requests.</summary>
internal sealed class GraphQlListener : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Queue<string> _queued = new();
    private readonly List<string> _bodies = [];
    private readonly List<string> _paths = [];
    private readonly Lock _sync = new();

    public GraphQlListener()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        BaseUrl = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(BaseUrl);
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    public string BaseUrl { get; }

    public IReadOnlyList<string> Bodies
    {
        get
        {
            lock (_sync)
            {
                return _bodies.ToArray();
            }
        }
    }

    public IReadOnlyList<string> Paths
    {
        get
        {
            lock (_sync)
            {
                return _paths.ToArray();
            }
        }
    }

    public void RespondInOrder(params string[] bodies)
    {
        lock (_sync)
        {
            foreach (var body in bodies)
            {
                _queued.Enqueue(body);
            }
        }
    }

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
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            string response;
            lock (_sync)
            {
                _bodies.Add(body);
                _paths.Add(context.Request.Url!.AbsolutePath);
                response = _queued.Count > 0 ? _queued.Dequeue() : "{}";
            }

            var bytes = Encoding.UTF8.GetBytes(response);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }
}
