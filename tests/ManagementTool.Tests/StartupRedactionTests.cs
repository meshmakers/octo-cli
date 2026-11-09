using Meshmakers.Octo.Frontend.ManagementTool.Services;
using NLog.Config;
using NLogLogManager = NLog.LogManager;

namespace ManagementTool.Tests;

/// <summary>
///     AB#8570: the redaction is active with the shipped <c>nlog.config</c> the way <c>Program.Main</c> starts:
///     <see cref="RedactedLogOutput.Enable" /> first, then the first logger, writing through the real console target.
///     Both orders are covered: configuration loaded before <c>Enable</c> (auto-load on first access) and loaded
///     afterwards (a reload). The built <c>octo-cli</c> is self-contained in Release, so it cannot be started with
///     <c>dotnet octo-cli.dll</c> from the tests; the smoke run of the binary is in the PR. Only fake values are used.
/// </summary>
[Collection("NLog configuration")]
public sealed class StartupRedactionTests
{
    private const string Token = "test-token-redact-me";

    private static string ShippedConfigPath => Path.Combine(AppContext.BaseDirectory, "nlog.config");

    [Fact]
    public void ShippedNLogConfig_LoadedBeforeEnable_ConsoleOutputIsRedacted() =>
        AssertConsoleIsRedacted(() =>
        {
            NLogLogManager.Configuration = new XmlLoggingConfiguration(ShippedConfigPath);
            RedactedLogOutput.Enable();
        });

    [Fact]
    public void ShippedNLogConfig_LoadedAfterEnable_ConsoleOutputIsRedacted() =>
        AssertConsoleIsRedacted(() =>
        {
            RedactedLogOutput.Enable();
            NLogLogManager.Configuration = new XmlLoggingConfiguration(ShippedConfigPath);
        });

    private static void AssertConsoleIsRedacted(Action start)
    {
        Assert.True(File.Exists(ShippedConfigPath), $"nlog.config not found next to the tests: {ShippedConfigPath}");

        var previousConfiguration = NLogLogManager.Configuration;
        var previousOut = Console.Out;
        var previousError = Console.Error;
        var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Console.SetError(output);

            start();
            var logger = NLogLogManager.GetLogger("Meshmakers.Octo.Frontend.ManagementTool.Program");
            logger.Error("{Message}", "Context 'missing client_secret=" + Token + "' does not exist.");
            logger.Error(new HttpRequestException("Authorization: Bearer " + Token), "Stopped program because of exception");
            NLogLogManager.Flush();
        }
        finally
        {
            NLogLogManager.Configuration = previousConfiguration;
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }

        var text = output.ToString();
        Assert.Contains("Context 'missing client_secret=[REDACTED]' does not exist.", text);
        Assert.Contains("Stopped program because of exception", text);
        Assert.Contains("HttpRequestException", text);
        Assert.DoesNotContain(Token, text);
    }
}
