using System.Diagnostics;

namespace ManagementTool.Tests;

/// <summary>
///     AB#8570: the redaction is active from the first log line of a real octo-cli process, with the shipped
///     <c>nlog.config</c> loaded by <c>Program.Main</c>. The test starts the built <c>octo-cli.dll</c> in a fresh process
///     with its own, empty configuration home (<c>OCTO_CLI_HOME</c>) and an unknown context whose name carries a fake
///     credential; <c>Program</c> logs the name in its error. Only fake values are used.
/// </summary>
public sealed class StartupRedactionTests : IDisposable
{
    private const string Token = "test-token-redact-me";

    private readonly string _home = Path.Combine(Path.GetTempPath(), "octo-cli-8570-" + Guid.NewGuid().ToString("N"));

    public StartupRedactionTests() => Directory.CreateDirectory(_home);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_home, true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    [Fact]
    public async Task FreshProcess_WithShippedNLogConfig_RedactsTheFirstErrorLine()
    {
        var cli = Path.Combine(AppContext.BaseDirectory, "octo-cli.dll");
        Assert.True(File.Exists(cli), $"octo-cli.dll not found next to the tests: {cli}");
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "nlog.config")));

        var start = new ProcessStartInfo(DotnetHost())
        {
            WorkingDirectory = _home,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(cli);
        start.ArgumentList.Add("--context");
        start.ArgumentList.Add("missing client_secret=" + Token + " Authorization: Bearer " + Token);
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("AuthStatus");
        start.Environment["OCTO_CLI_HOME"] = _home;
        start.Environment.Remove("OCTO_CLI_CONTEXT");

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        await process.WaitForExitAsync(timeout.Token);
        var output = await stdout + "\n" + await stderr;

        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("Context 'missing client_secret=[REDACTED]", output);
        Assert.DoesNotContain(Token, output);
    }

    // The muxer that runs the tests: "dotnet" from PATH can be another installation without this runtime (CI agents
    // install the SDK under ~/.dotnet). The dotnet CLI hands its own path to child processes in DOTNET_HOST_PATH.
    private static string DotnetHost()
    {
        var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(hostPath) && File.Exists(hostPath))
        {
            return hostPath;
        }

        var processPath = Environment.ProcessPath;
        return processPath != null &&
               Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? processPath
            : "dotnet";
    }
}
