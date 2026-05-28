using Meshmakers.Octo.Frontend.CommandReferenceGenerator;

namespace Meshmakers.Octo.Frontend.CommandReferenceGenerator.Tests;

public class RoslynExtractorTests
{
    [Fact]
    public void Extracts_shape_a_command_without_group()
    {
        var source = """
            namespace Test;
            internal class FooCommand : Command<X>
            {
                public FooCommand(ILogger<FooCommand> logger, IOptions<X> options)
                    : base(logger, "Foo", "Does foo.", options)
                {
                    CommandArgumentValue.AddArgument("f", "foo", ["foo help"], true, 1);
                }
            }
            """;

        var commands = RoslynExtractor.Extract(source);

        Assert.Single(commands);
        var cmd = commands[0];
        Assert.Null(cmd.Group);
        Assert.Equal("Foo", cmd.Verb);
        Assert.Equal("Does foo.", cmd.Description);
        Assert.Single(cmd.Args);
        Assert.Equal(new ArgumentDescriptor("f", "foo", "foo help", IsRequired: true, ValueCount: 1), cmd.Args[0]);
    }

    [Fact]
    public void Extracts_shape_b_command_with_literal_group()
    {
        // Shape B with literal group: some authors pass the group as a string literal
        // instead of a Constants.X reference. The extractor must NOT mistake this for
        // Shape A (where arg1 would be the verb).
        var source = """
            namespace Test;
            internal class FooCommand : ServiceClientCommand<X>
            {
                public FooCommand(ILogger<FooCommand> logger, IOptions<X> options, X x)
                    : base(logger, "Some Literal Group", "Foo", "Does foo.", options, x)
                {
                    CommandArgumentValue.AddArgument("f", "foo", ["foo help"], true, 1);
                }
            }
            """;

        var commands = RoslynExtractor.Extract(source);

        Assert.Single(commands);
        var cmd = commands[0];
        Assert.Equal("Some Literal Group", cmd.Group);
        Assert.Equal("Foo", cmd.Verb);
        Assert.Equal("Does foo.", cmd.Description);
    }

    [Fact]
    public void Extracts_shape_b_command_with_group()
    {
        var source = """
            namespace Test;
            internal class FooCommand : ServiceClientCommand<X>
            {
                public FooCommand(ILogger<FooCommand> logger, IOptions<X> options, X x)
                    : base(logger, Constants.MyGroup, "Foo", "Does foo.", options, x)
                {
                    CommandArgumentValue.AddArgument("f", "foo", ["foo help"], true, 1);
                }
            }
            """;

        var commands = RoslynExtractor.Extract(source);

        Assert.Single(commands);
        var cmd = commands[0];
        Assert.Equal("MyGroup", cmd.Group);
        Assert.Equal("Foo", cmd.Verb);
        Assert.Equal("Does foo.", cmd.Description);
    }

    [Fact]
    public void Detects_4_arg_AddArgument_overload_as_optional()
    {
        var source = """
            namespace Test;
            internal class FooCommand : Command<X>
            {
                public FooCommand(ILogger<FooCommand> logger, IOptions<X> options)
                    : base(logger, "Foo", "Does foo.", options)
                {
                    CommandArgumentValue.AddArgument("f", "foo", ["foo help"], 1);
                }
            }
            """;

        var commands = RoslynExtractor.Extract(source);

        Assert.Single(commands);
        var arg = commands[0].Args[0];
        Assert.False(arg.IsRequired);
        Assert.Equal(1, arg.ValueCount);
    }

    [Fact]
    public void Joins_multiline_help_with_newline()
    {
        var source = """
            namespace Test;
            internal class FooCommand : Command<X>
            {
                public FooCommand(ILogger<FooCommand> logger, IOptions<X> options)
                    : base(logger, "Foo", "Does foo.", options)
                {
                    CommandArgumentValue.AddArgument("e", "emergency",
                        ["First line", "Second line"], false, 0);
                }
            }
            """;

        var commands = RoslynExtractor.Extract(source);

        Assert.Equal("First line\nSecond line", commands[0].Args[0].Help);
    }

    [Fact]
    public void Skips_class_without_base_initializer()
    {
        var source = """
            namespace Test;
            internal static class Helper
            {
                public static string Foo(string s) => s.ToUpper();
            }
            """;

        var commands = RoslynExtractor.Extract(source);

        Assert.Empty(commands);
    }

    [Fact]
    public void Skips_commented_out_class()
    {
        var source = """
            namespace Test;
            /*
            internal class GhostCommand : Command<X>
            {
                public GhostCommand(...)
                    : base(logger, "Ghost", "Ghost.", options)
                {
                    CommandArgumentValue.AddArgument("g", "ghost", ["g"], true, 1);
                }
            }
            */
            internal static class Marker { }
            """;

        var commands = RoslynExtractor.Extract(source);

        Assert.Empty(commands);
    }

    [Fact]
    public void InheritedArgsByBaseClass_does_not_drift_from_JobWithWaitOctoCommand_source()
    {
        // Drift guard: RoslynExtractor.InheritedArgsByBaseClass hardcodes the wait-arg signature
        // mirroring JobWithWaitOctoCommand.cs. If the base class adds, removes, or changes args,
        // this test fails and reminds you to update the hardcoded mirror.
        var root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "Octo.Cli.sln")))
            root = Directory.GetParent(root)?.FullName;
        Assert.NotNull(root);
        var sourcePath = Path.Combine(root!, "src", "ManagementTool", "Commands", "JobWithWaitOctoCommand.cs");
        Assert.True(File.Exists(sourcePath), $"Expected JobWithWaitOctoCommand.cs at {sourcePath}");

        var src = File.ReadAllText(sourcePath);

        // Hardcoded mirror expects exactly one AddArgument with these literals.
        // If you change either side, change both.
        Assert.Contains("AddArgument(\"w\", \"wait\"", src);
        Assert.Contains("\"Wait for a import job to complete\"", src);
        // No other AddArgument calls in this base class — keep it that way or update mirror.
        var addArgumentCount = System.Text.RegularExpressions.Regex.Matches(src, @"\.AddArgument\(").Count;
        Assert.Equal(1, addArgumentCount);
    }

    [Fact]
    public void Appends_inherited_args_from_known_base_class()
    {
        var source = """
            namespace Test;
            internal class FooCommand : JobWithWaitOctoCommand
            {
                public FooCommand(ILogger<FooCommand> logger, IOptions<X> options, IY y)
                    : base(logger, Constants.Group, "Foo", "Does foo.", options, y)
                {
                    CommandArgumentValue.AddArgument("p", "plan", ["plan help"], true, 1);
                }
            }
            """;

        var commands = RoslynExtractor.Extract(source);

        Assert.Single(commands);
        var cmd = commands[0];
        Assert.Equal(2, cmd.Args.Count);
        Assert.Equal("plan", cmd.Args[0].Long);
        Assert.Equal("wait", cmd.Args[1].Long);
        Assert.False(cmd.Args[1].IsRequired);
        Assert.Equal(0, cmd.Args[1].ValueCount);
        Assert.Equal("Wait for a import job to complete", cmd.Args[1].Help);
    }

    [Fact]
    public void Detects_4_arg_AddArgument_with_bool_as_flag_only()
    {
        var source = """
            namespace Test;
            internal class FooCommand : Command<X>
            {
                public FooCommand(ILogger<FooCommand> logger, IOptions<X> options)
                    : base(logger, "Foo", "Does foo.", options)
                {
                    CommandArgumentValue.AddArgument("f", "flag", ["a flag"], false);
                }
            }
            """;

        var commands = RoslynExtractor.Extract(source);

        Assert.Single(commands);
        var arg = commands[0].Args[0];
        Assert.False(arg.IsRequired);
        Assert.Equal(0, arg.ValueCount);
        Assert.Equal("flag", arg.Long);
    }

    [Fact]
    public void Resolves_group_constant_from_separate_source()
    {
        var constantsSource = """
            namespace Test;
            internal static class Constants
            {
                public const string MyGroup = "myGroupValue";
            }
            """;
        var commandSource = """
            namespace Test;
            internal class FooCommand : ServiceClientCommand<X>
            {
                public FooCommand(ILogger<FooCommand> logger, IOptions<X> options, X x)
                    : base(logger, Constants.MyGroup, "Foo", "Does foo.", options, x)
                {
                    CommandArgumentValue.AddArgument("f", "foo", ["foo help"], true, 1);
                }
            }
            """;

        var constants = RoslynExtractor.CollectConstants(constantsSource);
        var commands = RoslynExtractor.Extract(commandSource, constants);

        Assert.Single(commands);
        Assert.Equal("myGroupValue", commands[0].Group);
    }

    [Fact]
    public void Falls_back_to_identifier_text_when_constant_unresolved()
    {
        var source = """
            namespace Test;
            internal class FooCommand : ServiceClientCommand<X>
            {
                public FooCommand(ILogger<FooCommand> logger, IOptions<X> options, X x)
                    : base(logger, Constants.UnknownGroup, "Foo", "Does foo.", options, x)
                {
                    CommandArgumentValue.AddArgument("f", "foo", ["foo help"], true, 1);
                }
            }
            """;

        // No constants collected — empty dict
        var commands = RoslynExtractor.Extract(source, new Dictionary<string, string>());

        Assert.Single(commands);
        Assert.Equal("UnknownGroup", commands[0].Group); // identifier fallback
    }

    [Fact]
    public void Captures_class_name_on_descriptor()
    {
        var source = """
            namespace Test;
            internal class FooCommand : Command<X>
            {
                public FooCommand(ILogger<FooCommand> logger, IOptions<X> options)
                    : base(logger, "Foo", "Does foo.", options)
                {
                    CommandArgumentValue.AddArgument("f", "foo", ["foo help"], true, 1);
                }
            }
            """;

        var commands = RoslynExtractor.Extract(source);

        Assert.Single(commands);
        Assert.Equal("FooCommand", commands[0].ClassName);
    }

    [Fact]
    public void Extracts_string_concatenation_in_help()
    {
        var source = """
            namespace Test;
            internal class FooCommand : Command<X>
            {
                public FooCommand(ILogger<FooCommand> logger, IOptions<X> options)
                    : base(logger, "Foo", "Does foo.", options)
                {
                    CommandArgumentValue.AddArgument("v", "verbose",
                        ["Enable " + "verbose " + "logging"], false, 0);
                }
            }
            """;

        var commands = RoslynExtractor.Extract(source);

        Assert.Single(commands);
        Assert.Equal("Enable verbose logging", commands[0].Args[0].Help);
    }

    [Fact]
    public void Detects_6_arg_AddArgument_overload()
    {
        var source = """
            namespace Test;
            internal class FooCommand : Command<X>
            {
                public FooCommand(ILogger<FooCommand> logger, IOptions<X> options)
                    : base(logger, "Foo", "Does foo.", options)
                {
                    CommandArgumentValue.AddArgument("o", "output", ["output path"], false, 1, true);
                }
            }
            """;

        var commands = RoslynExtractor.Extract(source);

        Assert.Single(commands);
        var arg = commands[0].Args[0];
        Assert.Equal("output", arg.Long);
        Assert.False(arg.IsRequired);
        Assert.Equal(1, arg.ValueCount);
    }

    [Fact]
    public void Extracts_interpolated_string_with_const_references()
    {
        var source = """
            namespace Test;
            internal static class Constants
            {
                public const string EnvVar = "MY_ENV";
            }
            internal class FooCommand : Command<X>
            {
                public FooCommand(ILogger<FooCommand> logger, IOptions<X> options)
                    : base(logger, "Foo", $"Reads from {Constants.EnvVar} env var.", options)
                {
                    CommandArgumentValue.AddArgument("v", "verbose", ["v"], false, 0);
                }
            }
            """;

        var constants = RoslynExtractor.CollectConstants(source);
        var commands = RoslynExtractor.Extract(source, constants);

        Assert.Single(commands);
        Assert.Equal("Reads from MY_ENV env var.", commands[0].Description);
    }
}
