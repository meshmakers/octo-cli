using Meshmakers.Octo.Frontend.ManagementTool.Services;

namespace ManagementTool.Tests;

/// <summary>
/// The context has to be resolved from the raw command line before the DI container exists, so this
/// pre-scan is the only thing standing between "--context prod" and a command silently running
/// against the active context. These tests pin the spellings it accepts and the precedence between
/// argument and environment variable.
/// </summary>
public sealed class ContextSelectionTests
{
    private const string EnvVarContext = "OCTO_CLI_CONTEXT";

    private static Func<string, string?> Environment(string? contextValue) =>
        name => name == EnvVarContext ? contextValue : null;

    // Index 0 is the executable name, mirroring Environment.GetCommandLineArgs().
    private static string[] CommandLine(params string[] arguments) => ["octo-cli", ..arguments];

    [Theory]
    [InlineData("--context")]
    [InlineData("-context")]
    [InlineData("/context")]
    [InlineData("--CONTEXT")]
    [InlineData("--Context")]
    public void Resolve_AcceptsEveryPrefixTheParserAccepts(string term)
    {
        var selection = ContextSelection.Resolve(CommandLine(term, "prod"), Environment(null));

        Assert.Equal("prod", selection.Name);
        Assert.True(selection.IsOverridden);
    }

    [Theory]
    // Argument.Compare compares at least ShortTerm.Length characters and both terms are "context",
    // so an abbreviation does not match — the pre-scan must not be more permissive than the parser.
    [InlineData("--contex")]
    [InlineData("--ctx")]
    [InlineData("--contextfoo")]
    [InlineData("-c")]
    public void Resolve_IgnoresTermsTheParserWouldNotMatch(string term)
    {
        var selection = ContextSelection.Resolve(CommandLine(term, "prod"), Environment(null));

        Assert.Null(selection.Name);
        Assert.False(selection.IsOverridden);
    }

    [Fact]
    public void Resolve_FindsArgumentAfterTheCommand()
    {
        // Unknown "-" terms bubble from the command layer back to the top layer, so the argument
        // works in any position; the pre-scan has to behave the same way.
        var selection = ContextSelection.Resolve(CommandLine("-c", "GetUsers", "--context", "prod"),
            Environment(null));

        Assert.Equal("prod", selection.Name);
    }

    [Fact]
    public void Resolve_FindsArgumentBeforeTheCommand()
    {
        var selection = ContextSelection.Resolve(CommandLine("--context", "prod", "-c", "GetUsers"),
            Environment(null));

        Assert.Equal("prod", selection.Name);
    }

    [Fact]
    public void Resolve_ArgumentWinsOverEnvironmentVariable()
    {
        var selection = ContextSelection.Resolve(CommandLine("--context", "prod"), Environment("dev"));

        Assert.Equal("prod", selection.Name);
        Assert.Equal("--context", selection.Source);
    }

    [Fact]
    public void Resolve_FallsBackToEnvironmentVariable()
    {
        var selection = ContextSelection.Resolve(CommandLine("-c", "GetUsers"), Environment("dev"));

        Assert.Equal("dev", selection.Name);
        Assert.Null(selection.CommandLineName);
        Assert.Equal(EnvVarContext, selection.Source);
    }

    [Fact]
    public void Resolve_WithoutAnySource_IsNotOverridden()
    {
        var selection = ContextSelection.Resolve(CommandLine("-c", "GetUsers"), Environment(null));

        Assert.Same(ContextSelection.None, selection);
        Assert.False(selection.IsOverridden);
    }

    [Fact]
    public void Resolve_TrimsAndIgnoresBlankValues()
    {
        Assert.Equal("prod", ContextSelection.Resolve(CommandLine("--context", " prod "), Environment(null)).Name);
        Assert.Equal("dev", ContextSelection.Resolve(CommandLine(), Environment("  dev ")).Name);
        Assert.Null(ContextSelection.Resolve(CommandLine("--context", "   "), Environment(null)).Name);
    }

    [Fact]
    public void Resolve_ArgumentWithoutValue_IsIgnoredAndLeftToTheParser()
    {
        // The parser reports the missing mandatory value; the pre-scan must not guess one.
        var selection = ContextSelection.Resolve(CommandLine("-c", "GetUsers", "--context"), Environment(null));

        Assert.Null(selection.Name);
    }

    [Fact]
    public void Resolve_FirstOccurrenceWins()
    {
        // IArgumentValue.GetValue reads index 0, so the parser sees the first one too.
        var selection = ContextSelection.Resolve(CommandLine("--context", "prod", "--context", "dev"),
            Environment(null));

        Assert.Equal("prod", selection.Name);
    }

    [Fact]
    public void ResolveFromCommandLine_IgnoresTheExecutableName()
    {
        // Index 0 is skipped, so a term in that position is not read as the argument.
        Assert.Null(ContextOverrideResolver.ResolveFromCommandLine(["--context", "prod"]));
    }
}
