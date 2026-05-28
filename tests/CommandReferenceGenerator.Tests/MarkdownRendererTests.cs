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
    public void Renders_notes_section_when_sidecar_has_notes()
    {
        var cmd = new CommandDescriptor(
            Group: null,
            Verb: "Foo",
            Description: "Does foo.",
            Args: Array.Empty<ArgumentDescriptor>())
        {
            NotesMarkdown = "Requires authentication. Run `octo-cli LogIn` first."
        };

        var md = MarkdownRenderer.Render(cmd);

        Assert.Contains("## Notes\n\nRequires authentication. Run `octo-cli LogIn` first.\n", md);
    }

    [Fact]
    public void Omits_notes_section_when_sidecar_has_no_notes()
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
    public void Renders_see_also_section_when_sidecar_has_see_also()
    {
        var cmd = new CommandDescriptor(
            Group: null,
            Verb: "Foo",
            Description: "Does foo.",
            Args: Array.Empty<ArgumentDescriptor>())
        {
            SeeAlsoMarkdown = "- [LogIn](../general/LogIn.md)\n- [Bar](./Bar.md)"
        };

        var md = MarkdownRenderer.Render(cmd);

        Assert.Contains("## See Also\n\n- [LogIn](../general/LogIn.md)\n- [Bar](./Bar.md)\n", md);
    }

    [Fact]
    public void Omits_see_also_section_when_sidecar_has_no_see_also()
    {
        var cmd = new CommandDescriptor(
            Group: null,
            Verb: "Foo",
            Description: "Does foo.",
            Args: Array.Empty<ArgumentDescriptor>());

        var md = MarkdownRenderer.Render(cmd);

        Assert.DoesNotContain("## See Also", md);
    }

    [Fact]
    public void Renders_sidecar_examples_when_set_skipping_auto_canonical()
    {
        var cmd = new CommandDescriptor(
            Group: null,
            Verb: "Foo",
            Description: "Does foo.",
            Args: new[]
            {
                new ArgumentDescriptor("f", "foo", "foo help", IsRequired: true, ValueCount: 1)
            })
        {
            ExamplesMarkdown = "Custom example block:\n\n```bash\noctocli foo --special\n```"
        };

        var md = MarkdownRenderer.Render(cmd);

        Assert.Contains("Custom example block:", md);
        Assert.Contains("octocli foo --special", md);
        Assert.DoesNotContain("octo-cli -c Foo -f <foo>", md); // auto-canonical NOT present
    }
}
