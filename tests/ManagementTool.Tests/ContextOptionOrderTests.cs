using FakeItEasy;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;

namespace ManagementTool.Tests;

/// <summary>
/// AB#7414: the parser's top layer never hands control back to a command layer, so "--context" in the
/// middle of a command's options used to make the options after it "unknown parameters". The fix moves
/// the context argument to the front of the line before parsing; these tests run the real parser.
/// </summary>
public sealed class ContextOptionOrderTests
{
    private static string[] Line(params string[] arguments) => ["octo-cli", ..arguments];

    [Fact]
    public void Hoist_MovesContextInFrontOfTheCommandOptions_KeepingTheRestInOrder()
    {
        var hoisted = ContextArgumentHoister.Hoist(Line("-c", "FixAll", "--context", "x", "-w", "-y"));

        Assert.Equal(Line("--context", "x", "-c", "FixAll", "-w", "-y"), hoisted);
    }

    [Fact]
    public void Hoist_LeavesALineWithoutContextUntouched()
    {
        var line = Line("-c", "FixAll", "-w", "-y");

        Assert.Equal(line, ContextArgumentHoister.Hoist(line));
    }

    [Fact]
    public void Hoist_LeavesAContextTermWithoutValueInPlace()
    {
        var line = Line("-c", "FixAll", "-w", "--context");

        Assert.Equal(line, ContextArgumentHoister.Hoist(line));
    }

    [Fact]
    public void Hoist_HandlesAnEmptyCommandLine()
    {
        Assert.Empty(ContextArgumentHoister.Hoist([]));
    }

    [Theory]
    [InlineData("--context", "x", "-c", "FixAll", "-w", "-y", "-n", "abc")] // before
    [InlineData("-c", "FixAll", "--context", "x", "-w", "-y", "-n", "abc")] // right after the command
    [InlineData("-c", "FixAll", "-w", "--context", "x", "-y", "-n", "abc")] // between flags
    [InlineData("-c", "FixAll", "-w", "-y", "--context", "x", "-n", "abc")] // before a valued option
    [InlineData("-c", "FixAll", "-w", "-y", "-n", "abc", "--context", "x")] // after
    [InlineData("-c", "FixAll", "--context", "x", "--wait", "--yes", "--name", "abc")] // long terms
    [InlineData("-c", "FixAll", "-n", "abc", "--context", "x", "-w", "-y")] // after a valued option
    public void Parse_AcceptsTheContextAtAnyPosition(params string[] arguments)
    {
        var (parser, context, wait, yes, name) = NewParser(Line(arguments));

        parser.ParseAndValidate();

        Assert.Equal("x", parser.GetArgumentValue(context).GetValue(string.Empty));
        var command = FixAllValue(parser);
        Assert.True(command.IsArgumentUsed(wait));
        Assert.True(command.IsArgumentUsed(yes));
        Assert.Equal("abc", command.GetArgumentValue(name).GetValue(string.Empty));
    }

    [Fact]
    public void Parse_StillRejectsAnUnknownOptionOfTheCommand()
    {
        var (parser, _, _, _, _) = NewParser(Line("-c", "FixAll", "--context", "x", "--nonsense"));

        var exception = Assert.Throws<InvalidParameterException>(parser.ParseAndValidate);

        Assert.Contains("--nonsense", exception.Message);
    }

    private static ICommandArgumentValue FixAllValue(ParserService parser)
    {
        var commandArgument = parser.GetType()
            .GetProperty("CommandArgument", System.Reflection.BindingFlags.NonPublic |
                                            System.Reflection.BindingFlags.Instance)!
            .GetValue(parser) as ICommandArgument;
        Assert.NotNull(commandArgument);
        Assert.True(commandArgument.TryGetCommandValue("FixAll", out var value));
        return value!;
    }

    private static (ParserService Parser, IArgument Context, IArgument Wait, IArgument Yes, IArgument Name)
        NewParser(string[] rawLine)
    {
        var environment = A.Fake<IEnvironmentService>();
        A.CallTo(() => environment.GetCommandLineArgs()).Returns(rawLine);
        var parser = new ParserService(new ContextHoistingEnvironmentService(environment),
            A.Fake<IConsoleService>());

        var commandArgument = parser.AddCommandArgument("c", "command", ["Command"], true);
        var fixAll = new CommandArgumentValue("General", "FixAll", "Fix all");
        var wait = fixAll.AddArgument("w", "wait", ["Wait"]);
        var yes = fixAll.AddArgument("y", "yes", ["Yes"]);
        var name = fixAll.AddArgument("n", "name", ["Name"], 1);
        commandArgument.AddCommandValue(fixAll);
        var context = parser.AddArgument("context", "context", ["Context"], 1);

        return (parser, context, wait, yes, name);
    }
}
