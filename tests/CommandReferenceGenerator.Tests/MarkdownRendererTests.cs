using Meshmakers.Octo.Frontend.CommandReferenceGenerator;

namespace Meshmakers.Octo.Frontend.CommandReferenceGenerator.Tests;

public class MarkdownRendererTests
{
    [Fact]
    public void Renders_command_without_group()
    {
        var cmd = new CommandDescriptor(
            Group: null,
            Verb: "LaunchAttack",
            Description: "Initiates an attack run on the specified target.",
            Args: new[]
            {
                new ArgumentDescriptor("t", "target", "Target identifier (e.g. 'venator-01')", IsRequired: true, ValueCount: 1),
                new ArgumentDescriptor("i", "intensity", "Attack intensity level (default: 'low')", IsRequired: false, ValueCount: 1),
            });

        var md = MarkdownRenderer.Render(cmd);

        var expected =
            "---\n" +
            "title: LaunchAttack\n" +
            "tags:\n" +
            "  - technology\n" +
            "  - tools\n" +
            "---\n" +
            "\n" +
            "# LaunchAttack\n" +
            "\n" +
            "Initiates an attack run on the specified target.\n" +
            "\n" +
            "## Examples\n" +
            "\n" +
            "```powershell\n" +
            "octo-cli -c LaunchAttack -t <target>\n" +
            "```\n" +
            "\n" +
            "## Options\n" +
            "\n" +
            "| Short | Long | Required | Description |\n" +
            "|-------|------|----------|-------------|\n" +
            "| `-t` | `--target` | yes | Target identifier (e.g. 'venator-01') |\n" +
            "| `-i` | `--intensity` | no | Attack intensity level (default: 'low') |\n";

        Assert.Equal(expected, md);
    }

    [Fact]
    public void Emits_frontmatter_with_title_and_tags()
    {
        var cmd = new CommandDescriptor(
            Group: "AnyGroup",
            Verb: "Foo",
            Description: "Does foo.",
            Args: Array.Empty<ArgumentDescriptor>());

        var md = MarkdownRenderer.Render(cmd);

        Assert.StartsWith("---\ntitle: Foo\ntags:\n  - technology\n  - tools\n---\n\n# Foo\n", md);
    }

    [Fact]
    public void Group_is_not_rendered_in_markdown_body()
    {
        var cmd = new CommandDescriptor(
            Group: "SeparatistGroup",
            Verb: "DeployToBattlefield",
            Description: "Deploys a vulture droid wave to the specified sector.",
            Args: new[]
            {
                new ArgumentDescriptor("s", "sector", "Sector ID (e.g. 'naboo-east')", IsRequired: true, ValueCount: 1),
                new ArgumentDescriptor("cf", "coverFire", "Enable cover fire from control ship", IsRequired: false, ValueCount: 0),
            });

        var md = MarkdownRenderer.Render(cmd);

        var expected =
            "---\n" +
            "title: DeployToBattlefield\n" +
            "tags:\n" +
            "  - technology\n" +
            "  - tools\n" +
            "---\n" +
            "\n" +
            "# DeployToBattlefield\n" +
            "\n" +
            "Deploys a vulture droid wave to the specified sector.\n" +
            "\n" +
            "## Examples\n" +
            "\n" +
            "```powershell\n" +
            "octo-cli -c DeployToBattlefield -s <sector>\n" +
            "```\n" +
            "\n" +
            "## Options\n" +
            "\n" +
            "| Short | Long | Required | Description |\n" +
            "|-------|------|----------|-------------|\n" +
            "| `-s` | `--sector` | yes | Sector ID (e.g. 'naboo-east') |\n" +
            "| `-cf` | `--coverFire` | no | Enable cover fire from control ship |\n";

        Assert.Equal(expected, md);
        Assert.DoesNotContain("**Group:**", md);
    }

    [Fact]
    public void Renders_no_options_message_when_args_empty()
    {
        var cmd = new CommandDescriptor(
            Group: null,
            Verb: "Status",
            Description: "Shows the current status.",
            Args: Array.Empty<ArgumentDescriptor>());

        var md = MarkdownRenderer.Render(cmd);

        var expected =
            "---\n" +
            "title: Status\n" +
            "tags:\n" +
            "  - technology\n" +
            "  - tools\n" +
            "---\n" +
            "\n" +
            "# Status\n" +
            "\n" +
            "Shows the current status.\n" +
            "\n" +
            "## Examples\n" +
            "\n" +
            "```powershell\n" +
            "octo-cli -c Status\n" +
            "```\n" +
            "\n" +
            "## Options\n" +
            "\n" +
            "_This command takes no options._\n";

        Assert.Equal(expected, md);
    }

    [Fact]
    public void Renders_multiline_help_with_br_tag()
    {
        var cmd = new CommandDescriptor(
            Group: null,
            Verb: "Foo",
            Description: "test.",
            Args: new[]
            {
                new ArgumentDescriptor("e", "emergency", "First line\nSecond line", IsRequired: false, ValueCount: 0),
            });

        var md = MarkdownRenderer.Render(cmd);

        Assert.Contains("| `-e` | `--emergency` | no | First line<br/>Second line |\n", md);
    }

    [Fact]
    public void Renders_notes_section_when_command_has_notes()
    {
        var cmd = new CommandDescriptor(
            Group: null,
            Verb: "Foo",
            Description: "Does foo.",
            Args: Array.Empty<ArgumentDescriptor>())
        {
            Notes = new[] { "Requires authentication. Run `octo-cli LogIn` first." }
        };

        var md = MarkdownRenderer.Render(cmd);

        Assert.Contains("## Notes\n\nRequires authentication. Run `octo-cli LogIn` first.\n", md);
    }

    [Fact]
    public void Omits_notes_section_when_command_has_no_notes()
    {
        var cmd = new CommandDescriptor(
            Group: null,
            Verb: "Foo",
            Description: "Does foo.",
            Args: Array.Empty<ArgumentDescriptor>());

        var md = MarkdownRenderer.Render(cmd);

        Assert.DoesNotContain("## Notes", md);
    }

    [Fact]
    public void Composes_invocation_from_argument_bindings()
    {
        var fooArg = new ArgumentDescriptor("f", "foo", "foo help", IsRequired: true, ValueCount: 1);
        var cmd = new CommandDescriptor(
            Group: null,
            Verb: "Bar",
            Description: "Bars.",
            Args: new[] { fooArg })
        {
            Samples = new[]
            {
                new SampleDescriptor(
                    new[] { new SampleArgumentBinding(fooArg, "value-1") },
                    "Basic usage"),
            }
        };

        var md = MarkdownRenderer.Render(cmd);

        Assert.Contains("Basic usage:", md);
        Assert.Contains("octo-cli -c Bar -f \"value-1\"", md);
    }

    [Fact]
    public void Composes_invocation_multiline_when_three_or_more_bindings()
    {
        var aArg = new ArgumentDescriptor("a", "alpha", "a help", IsRequired: true, ValueCount: 1);
        var bArg = new ArgumentDescriptor("b", "beta", "b help", IsRequired: true, ValueCount: 1);
        var cArg = new ArgumentDescriptor("c", "gamma", "c help", IsRequired: true, ValueCount: 1);
        var cmd = new CommandDescriptor(
            Group: null,
            Verb: "Multi",
            Description: "Multi.",
            Args: new[] { aArg, bArg, cArg })
        {
            Samples = new[]
            {
                new SampleDescriptor(
                    new[]
                    {
                        new SampleArgumentBinding(aArg, "v1"),
                        new SampleArgumentBinding(bArg, "v2"),
                        new SampleArgumentBinding(cArg, "v3"),
                    },
                    "Three bindings"),
            }
        };

        var md = MarkdownRenderer.Render(cmd);

        // Multi-line uses PowerShell-7 backtick line-continuation with 4-space indent.
        Assert.Contains("octo-cli -c Multi `\n    -a \"v1\" `\n    -b \"v2\" `\n    -c \"v3\"", md);
    }

    [Fact]
    public void Description_with_trailing_punctuation_does_not_get_extra_colon()
    {
        var arg = new ArgumentDescriptor("f", "foo", "foo", IsRequired: true, ValueCount: 1);
        var cmd = new CommandDescriptor(
            Group: null,
            Verb: "Foo",
            Description: "Does foo.",
            Args: new[] { arg })
        {
            Samples = new[]
            {
                new SampleDescriptor(
                    new[] { new SampleArgumentBinding(arg, "x") },
                    "Ends with a period."),
            }
        };

        var md = MarkdownRenderer.Render(cmd);

        Assert.Contains("Ends with a period.\n", md);
        Assert.DoesNotContain("Ends with a period.:", md);
    }

    [Fact]
    public void Composes_flag_without_value()
    {
        var waitArg = new ArgumentDescriptor("w", "wait", "Wait for completion", IsRequired: false, ValueCount: 0);
        var cmd = new CommandDescriptor(
            Group: null,
            Verb: "Run",
            Description: "Runs.",
            Args: new[] { waitArg })
        {
            Samples = new[]
            {
                new SampleDescriptor(
                    new[] { new SampleArgumentBinding(waitArg, null) },
                    "Wait inline"),
            }
        };

        var md = MarkdownRenderer.Render(cmd);

        Assert.Contains("octo-cli -c Run -w", md);
        // Flag must not be followed by a quoted value
        Assert.DoesNotContain("-w \"", md);
    }

    [Fact]
    public void Renders_expected_output_block()
    {
        var cmd = new CommandDescriptor(
            Group: null,
            Verb: "Status",
            Description: "Shows status.",
            Args: Array.Empty<ArgumentDescriptor>())
        {
            Samples = new[]
            {
                new SampleDescriptor(
                    Array.Empty<SampleArgumentBinding>(),
                    "Show current status",
                    "NAME   STATE\nfoo    OK"),
            }
        };

        var md = MarkdownRenderer.Render(cmd);

        Assert.Contains("**Output:**\n\n```\nNAME   STATE\nfoo    OK\n```", md);
    }
}
