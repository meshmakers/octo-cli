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

        sb.Append("## Examples\n");
        sb.Append('\n');
        if (cmd.ExamplesMarkdown != null)
        {
            sb.Append(cmd.ExamplesMarkdown);
            if (!cmd.ExamplesMarkdown.EndsWith('\n')) sb.Append('\n');
            sb.Append('\n');
        }
        else
        {
            sb.Append("```powershell\n");
            sb.Append(BuildCanonicalExample(cmd));
            sb.Append('\n');
            sb.Append("```\n");
            sb.Append('\n');
        }

        sb.Append("## Options\n");
        sb.Append('\n');
        if (cmd.Args.Count == 0)
        {
            sb.Append("_This command takes no options._\n");
        }
        else
        {
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

        if (cmd.NotesMarkdown != null)
        {
            sb.Append('\n');
            sb.Append("## Notes\n");
            sb.Append('\n');
            sb.Append(cmd.NotesMarkdown);
            if (!cmd.NotesMarkdown.EndsWith('\n')) sb.Append('\n');
        }

        if (cmd.SeeAlsoMarkdown != null)
        {
            sb.Append('\n');
            sb.Append("## See Also\n");
            sb.Append('\n');
            sb.Append(cmd.SeeAlsoMarkdown);
            if (!cmd.SeeAlsoMarkdown.EndsWith('\n')) sb.Append('\n');
        }

        return sb.ToString();
    }

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
