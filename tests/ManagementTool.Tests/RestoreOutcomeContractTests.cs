using System.Net;
using System.Net.Sockets;
using System.Text;
using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     Contract test for <c>Restore -w</c> (AB#6969): the job result is read by the real <see cref="BotServicesClient" />
///     from the exact wire form the bot sends (<c>GET system/v1/jobs?id=…</c>, camelCase) and fed into the command, so a
///     rename on either side of the contract fails here. The payloads (Fixtures/*.json) are the ones captured in the bot tests
///     (<c>RestoreOutcomeTests</c>, <c>CapturedFailedArchiveResult</c> / <c>CapturedCleanResult</c>).
/// </summary>
public sealed class RestoreOutcomeContractTests : IDisposable
{
    // Sanitised results as the bot writes them (ASP.NET web defaults: camelCase, nulls included, derived hasWarnings).
    private static string FailedArchiveResult => Fixture("restore-job-failed-archive.json");

    private static string CleanResult => Fixture("restore-job-clean.json");

    private static string InformationalResult => Fixture("restore-job-informational.json");

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private readonly string _file = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".tar.gz");

    public RestoreOutcomeContractTests() => File.WriteAllText(_file, "payload");

    public void Dispose()
    {
        File.Delete(_file);
        foreach (var server in _servers)
        {
            server.Dispose();
        }
    }

    [Fact]
    public async Task Restore_Wait_CapturedFailedArchiveResult_ThrowsWithTheArchiveAndTheRollup()
    {
        var (command, logger) = await NewRestoreAsync(FailedArchiveResult);

        var exception = await Assert.ThrowsAsync<ToolException>(() => command.Execute());

        // Runner maps every ToolException to exit code -5 (Runner.cs, catch ToolException).
        Assert.Contains(
            "restored with 1 failed archive(s): ecaa00000000000000000a03; 1 rollup(s) not checked: ecaa00000000000000000a03 [ArchiveFailed]",
            exception.Message);
        Assert.DoesNotContain(logger.Entries, e => e.Message.EndsWith(" restored", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Restore_Wait_CapturedCleanResult_IsRestored()
    {
        var (command, logger) = await NewRestoreAsync(CleanResult);

        await command.Execute();

        Assert.Contains(logger.Entries, e => e.Message.EndsWith(" restored", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Restore_Wait_InformationalNotCheckedResult_IsRestoredAndShowsTheInfo()
    {
        var (command, logger) = await NewRestoreAsync(InformationalResult);

        await command.Execute();

        Assert.Contains(logger.Entries, e => e.Message.EndsWith(" restored", StringComparison.Ordinal));
        Assert.Contains(logger.Entries,
            e => e.Message.Contains("(info)") && e.Message.Contains("ecab00000000000000000a02"));
    }

    private async Task<(RestoreTenant Command, RecordingLogger<RestoreTenant> Logger)> NewRestoreAsync(string jobJson)
    {
        var server = new JsonServer(jobJson);
        _servers.Add(server);

        // The real client reads the job from the loopback server; only the tus upload is faked.
        var real = new BotServicesClient(new BotServiceClientOptions { EndpointUri = server.BaseUrl },
            A.Fake<IBotServiceClientAccessToken>());
        var bot = A.Fake<IBotServicesClient>();
        A.CallTo(() => bot.RestoreRepositoryWithTusAsync(A<string>._, A<string>._, A<string>._, A<string?>._,
                A<bool>._, A<bool>._, A<Action<double>?>._, A<CancellationToken>._))
            .Returns(new JobResponseDto("job-1"));
        A.CallTo(() => bot.GetImportJobStatus(A<string>._)).ReturnsLazily((string id) => real.GetImportJobStatus(id));

        var logger = new RecordingLogger<RestoreTenant>();
        var command = new RestoreTenant(logger, Options.Create(new OctoToolOptions { TenantId = "acme" }), bot,
            A.Fake<IAuthenticationService>(), Options.Create(new BotServiceClientOptions()));
        command.CommandArgumentValue.ParseLayer(["-tid", "acme", "-db", "db", "-f", _file, "-w"]);
        await Task.CompletedTask;
        return (command, logger);
    }

    private readonly List<JsonServer> _servers = [];

    /// <summary>Answers every request with the given JSON body (HTTP/1.1, connection closed after each answer).</summary>
    private sealed class JsonServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();

        public JsonServer(string body)
        {
            _listener.Start();
            BaseUrl = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        var tcp = await _listener.AcceptTcpClientAsync(_stop.Token);
                        _ = Task.Run(() => ServeAsync(tcp, body));
                    }
                }
                catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
                {
                }
            });
        }

        public string BaseUrl { get; }

        private static async Task ServeAsync(TcpClient tcp, string body)
        {
            using (tcp)
            {
                try
                {
                    var stream = tcp.GetStream();
                    var buffer = new byte[4096];
                    var request = new StringBuilder();
                    while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                    {
                        var read = await stream.ReadAsync(buffer);
                        if (read == 0)
                        {
                            return;
                        }

                        request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                    }

                    var bytes = Encoding.UTF8.GetBytes(body);
                    var head = Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(head);
                    await stream.WriteAsync(bytes);
                    await stream.FlushAsync();
                }
                catch (IOException)
                {
                }
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _stop.Dispose();
        }
    }
}
