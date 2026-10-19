using Meshmakers.Octo.Frontend.ManagementTool.Services;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace ManagementTool.Tests;

/// <summary>AB#7027: standard output stays free for the answer of a piped command.</summary>
[Collection("NLog configuration")]
public sealed class MachineReadableOutputTests
{
    [Theory]
    [InlineData("octo-cli", "-c", "GraphQl", "-tid", "x", "-q", "{a}")]
    [InlineData("octo-cli", "--context", "ctx", "-c", "graphql", "-q", "{a}")]
    [InlineData("octo-cli", "--command", "GRAPHQL")]
    [InlineData("octo-cli", "-tid", "x", "-c", "GraphQl ")]
    public void GraphQlCommand_IsRecognised(params string[] args) =>
        Assert.True(MachineReadableOutput.IsRequested(args));

    [Theory]
    [InlineData("octo-cli", "-c", "LibraryStatus")]
    [InlineData("octo-cli", "-c", "GraphQlDtos")]
    [InlineData("octo-cli", "-q", "GraphQl")]
    [InlineData("octo-cli", "-c")]
    [InlineData("octo-cli")]
    public void OtherCommands_AreNot(params string[] args) =>
        Assert.False(MachineReadableOutput.IsRequested(args));

    [Fact]
    public void RouteLogsToStandardError_SwitchesTheConsoleTargets()
    {
        var previous = LogManager.Configuration;
        try
        {
            var colored = new ColoredConsoleTarget("c");
            var plain = new ConsoleTarget("p");
            var file = new FileTarget("f") { FileName = "unused.log" };
            var configuration = new LoggingConfiguration();
            configuration.AddRule(LogLevel.Info, LogLevel.Fatal, colored);
            configuration.AddRule(LogLevel.Info, LogLevel.Fatal, plain);
            configuration.AddRule(LogLevel.Info, LogLevel.Fatal, file);
            LogManager.Configuration = configuration;

            MachineReadableOutput.RouteLogsToStandardError();
            MachineReadableOutput.RouteLogsToStandardError();

            Assert.Equal("true", colored.StdErr?.Render(LogEventInfo.CreateNullEvent()), ignoreCase: true);
            Assert.Equal("true", plain.StdErr?.Render(LogEventInfo.CreateNullEvent()), ignoreCase: true);
        }
        finally
        {
            LogManager.Configuration = previous;
        }
    }
}
