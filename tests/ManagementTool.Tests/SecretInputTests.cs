using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Microsoft.Extensions.Logging;

namespace ManagementTool.Tests;

/// <summary>
///     AB#8044 — <see cref="SecretInput" /> reads a password or secret without putting it on the command line: deprecated
///     argument (warning, never the value in the log) > standard input > environment variable > hidden prompt (terminal
///     only, required secrets only).
/// </summary>
public sealed class SecretInputTests
{
    private const string EnvVar = "OCTO_CLI_TEST_SECRET";

    // Throwaway value, generated per test run.
    private readonly string _secret = "s-" + Guid.NewGuid().ToString("N");

    private readonly CommandArgumentValue _arguments = new("Test", "Test", "Test command");
    private readonly IArgument _legacy;
    private readonly IArgument _stdin;
    private readonly FakeSecretConsole _console = new();
    private readonly RecordingLogger<SecretInputTests> _logger = new();

    public SecretInputTests()
    {
        _legacy = _arguments.AddArgument("p", "password", ["Deprecated"], false, 1);
        _stdin = _arguments.AddArgument("ps", "password-stdin", ["stdin"], false, 0);
    }

    private SecretSpec Spec(bool required = true, bool confirm = false) =>
        new("password", _legacy, _stdin, EnvVar, required, confirm);

    private string? Resolve(SecretSpec spec, params string[] commandLine)
    {
        _arguments.ParseLayer(commandLine);
        return new SecretInput(_console).Resolve(_arguments, spec, _logger);
    }

    [Fact]
    public void DeprecatedArgument_StillWorks_AndWarnsWithoutTheValue()
    {
        var value = Resolve(Spec(), "-p", _secret);

        Assert.Equal(_secret, value);
        var warning = Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("deprecated", warning.Message);
        Assert.Contains("--password-stdin", warning.Message);
        Assert.Contains(EnvVar, warning.Message);
        Assert.DoesNotContain(_logger.Entries, e => e.Message.Contains(_secret));
        Assert.Empty(_console.Prompts);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void StandardInput_IsReadOnce_AndOneTrailingLineBreakIsRemoved(string lineBreak)
    {
        _console.IsInputRedirected = true;
        _console.StandardInput = _secret + lineBreak;
        _console.Environment[EnvVar] = "not-this-one";

        var value = Resolve(Spec(), "--password-stdin");

        Assert.Equal(_secret, value);
        Assert.Equal(1, _console.StandardInputReads);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public void StandardInput_KeepsInnerWhitespace()
    {
        _console.StandardInput = " a b \n";

        Assert.Equal(" a b ", Resolve(Spec(), "-ps"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    public void EmptyStandardInput_IsRefused(string input)
    {
        _console.StandardInput = input;

        var ex = Assert.Throws<ToolException>(() => Resolve(Spec(), "--password-stdin"));
        Assert.Contains("--password-stdin", ex.Message);
    }

    [Fact]
    public void ArgumentAndStandardInputTogether_AreRefused()
    {
        var ex = Assert.Throws<ToolException>(() => Resolve(Spec(), "-p", _secret, "--password-stdin"));

        Assert.Contains("-p", ex.Message);
        Assert.Contains("--password-stdin", ex.Message);
        Assert.DoesNotContain(_secret, ex.Message);
        Assert.Equal(0, _console.StandardInputReads);
    }

    [Fact]
    public void EnvironmentVariable_IsUsed_WhenNoFlagIsGiven_AndNothingIsPrompted()
    {
        _console.Environment[EnvVar] = _secret;

        Assert.Equal(_secret, Resolve(Spec()));
        Assert.Empty(_console.Prompts);
        Assert.Equal(0, _console.StandardInputReads);
    }

    [Fact]
    public void Terminal_PromptsHidden_WhenNoOtherSourceIsSet()
    {
        _console.Answer(_secret);

        Assert.Equal(_secret, Resolve(Spec()));
        Assert.Equal(["Password: "], _console.Prompts);
    }

    [Fact]
    public void Terminal_WithConfirmation_AsksTwice_AndAcceptsMatchingEntries()
    {
        _console.Answer(_secret, _secret);

        Assert.Equal(_secret, Resolve(Spec(confirm: true)));
        Assert.Equal(["Password: ", "Repeat password: "], _console.Prompts);
    }

    [Fact]
    public void Terminal_WithConfirmation_RefusesDifferentEntries()
    {
        _console.Answer(_secret, _secret + "x");

        var ex = Assert.Throws<ToolException>(() => Resolve(Spec(confirm: true)));
        Assert.Contains("do not match", ex.Message);
        Assert.DoesNotContain(_secret, ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Terminal_RefusesAnEmptyEntry(string? answer)
    {
        _console.Answer(answer);

        Assert.Throws<ToolException>(() => Resolve(Spec()));
    }

    [Fact]
    public void RedirectedInput_WithoutAnySource_FailsAndNamesTheAlternatives()
    {
        _console.IsInputRedirected = true;

        var ex = Assert.Throws<ToolException>(() => Resolve(Spec()));

        Assert.Contains("--password-stdin", ex.Message);
        Assert.Contains(EnvVar, ex.Message);
        Assert.Empty(_console.Prompts);
        Assert.Equal(0, _console.StandardInputReads);
    }

    [Fact]
    public void OptionalSecret_WithoutAnySource_IsNull_AndNeverPrompts()
    {
        Assert.Null(Resolve(Spec(required: false)));
        Assert.Empty(_console.Prompts);
    }

    [Fact]
    public void OptionalSecret_StillReadsTheEnvironmentVariable()
    {
        _console.Environment[EnvVar] = _secret;

        Assert.Equal(_secret, Resolve(Spec(required: false)));
    }
}
