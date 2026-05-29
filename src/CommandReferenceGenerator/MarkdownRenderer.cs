using System.Text;

namespace Meshmakers.Octo.Frontend.CommandReferenceGenerator;

public static class MarkdownRenderer
{
    public static string Render(CommandDescriptor cmd)
    {
        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append("title: ").Append(cmd.Verb).Append('\n');
        sb.Append("tags:\n");
        sb.Append("  - technology\n");
        sb.Append("  - tools\n");
        sb.Append("---\n");
        sb.Append('\n');
        sb.Append('#').Append(' ').Append(cmd.Verb).Append('\n');
        sb.Append('\n');
        sb.Append(cmd.Description).Append('\n');
        sb.Append('\n');

        AppendExamples(sb, cmd);
        AppendOptions(sb, cmd);
        AppendNotes(sb, cmd);
        AppendSeeAlso(sb, cmd);

        return sb.ToString();
    }

    private static void AppendExamples(StringBuilder sb, CommandDescriptor cmd)
    {
        sb.Append("## Examples\n");
        sb.Append('\n');

        if (cmd.Samples is { Count: > 0 })
        {
            for (var i = 0; i < cmd.Samples.Count; i++)
            {
                var sample = cmd.Samples[i];
                sb.Append(sample.Description);
                // Only append ":" if the description doesn't already end with terminal punctuation.
                if (sample.Description.Length > 0 && !".:!?".Contains(sample.Description[^1]))
                    sb.Append(':');
                sb.Append("\n\n");
                sb.Append("```powershell\n");
                sb.Append(ComposeInvocation(cmd.Verb, sample)).Append('\n');
                sb.Append("```\n");
                if (sample.ExpectedOutput is not null)
                {
                    sb.Append("\n**Output:**\n\n");
                    sb.Append("```\n");
                    sb.Append(sample.ExpectedOutput);
                    if (!sample.ExpectedOutput.EndsWith('\n')) sb.Append('\n');
                    sb.Append("```\n");
                }
                if (i < cmd.Samples.Count - 1) sb.Append('\n');
            }
            sb.Append('\n');
            return;
        }

        // Fallback: no samples declared — synthesize a canonical invocation from required args.
        sb.Append("```powershell\n");
        sb.Append(BuildCanonicalExample(cmd)).Append('\n');
        sb.Append("```\n");
        sb.Append('\n');
    }

    private static void AppendOptions(StringBuilder sb, CommandDescriptor cmd)
    {
        sb.Append("## Options\n");
        sb.Append('\n');
        if (cmd.Args.Count == 0)
        {
            sb.Append("_This command takes no options._\n");
            return;
        }

        sb.Append("| Short | Long | Required | Description |\n");
        sb.Append("|-------|------|----------|-------------|\n");
        foreach (var arg in cmd.Args)
        {
            var required = arg.IsRequired ? "yes" : "no";
            var helpForCell = arg.Help.Replace("\n", "<br/>");
            sb.Append("| `-").Append(arg.Short).Append("` | `--").Append(arg.Long)
              .Append("` | ").Append(required).Append(" | ").Append(helpForCell).Append(" |\n");
        }
    }

    private static void AppendNotes(StringBuilder sb, CommandDescriptor cmd)
    {
        if (cmd.Notes is not { Count: > 0 }) return;

        sb.Append('\n');
        sb.Append("## Notes\n");
        sb.Append('\n');
        foreach (var note in cmd.Notes)
        {
            sb.Append(note);
            if (!note.EndsWith('\n')) sb.Append('\n');
            sb.Append('\n');
        }
    }

    private static void AppendSeeAlso(StringBuilder sb, CommandDescriptor cmd)
    {
        if (cmd.SeeAlso is not { Count: > 0 }) return;

        sb.Append('\n');
        sb.Append("## See Also\n");
        sb.Append('\n');
        foreach (var link in cmd.SeeAlso)
        {
            sb.Append("- [").Append(link.Text).Append("](").Append(link.Url).Append(")\n");
        }
    }

    /// <summary>
    ///     Builds <c>octo-cli -c &lt;verb&gt; -&lt;short&gt; "value"...</c> from a sample's argument bindings.
    ///     Samples with three or more bindings render multi-line with PowerShell-7 backtick continuation
    ///     for readability; shorter invocations stay on a single line.
    /// </summary>
    private const int MultiLineBindingsThreshold = 3;

    private static string ComposeInvocation(string verb, SampleDescriptor sample)
    {
        var sb = new StringBuilder();
        sb.Append("octo-cli -c ").Append(verb);
        var multiLine = sample.Arguments.Count >= MultiLineBindingsThreshold;
        foreach (var binding in sample.Arguments)
        {
            sb.Append(multiLine ? " `\n    -" : " -").Append(binding.Argument.Short);
            if (binding.Value is not null)
                sb.Append(" \"").Append(binding.Value).Append('"');
        }
        return sb.ToString();
    }

    /// <summary>
    ///     Canonical example used when a command has no <c>GetDocumentation()</c> override:
    ///     just the verb with required arguments and placeholder long-name tokens.
    /// </summary>
    private static string BuildCanonicalExample(CommandDescriptor cmd)
    {
        var sb = new StringBuilder();
        sb.Append("octo-cli -c ").Append(cmd.Verb);
        foreach (var arg in cmd.Args)
        {
            if (!arg.IsRequired) continue;
            sb.Append(" -").Append(arg.Short);
            if (arg.ValueCount > 0)
            {
                sb.Append(" <").Append(arg.Long).Append('>');
            }
        }
        return sb.ToString();
    }
}
