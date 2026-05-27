using System.Text;

namespace Meshmakers.Octo.Frontend.CommandReferenceGenerator;

public static class SidecarLoader
{
    public sealed record SidecarContent(string? Examples, string? Notes, string? SeeAlso);

    private static readonly HashSet<string> RecognizedHeadings = new()
    {
        "## Examples",
        "## Notes",
        "## See Also",
    };

    public static SidecarContent Load(string? sourceFilePath, string? className)
    {
        if (string.IsNullOrEmpty(sourceFilePath) || string.IsNullOrEmpty(className))
            return new SidecarContent(null, null, null);

        var dir = Path.GetDirectoryName(sourceFilePath);
        if (dir == null) return new SidecarContent(null, null, null);

        var sidecarPath = Path.Combine(dir, $"{className}.docs.md");
        if (!File.Exists(sidecarPath)) return new SidecarContent(null, null, null);

        var content = File.ReadAllText(sidecarPath);
        return Parse(content);
    }

    public static SidecarContent Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new SidecarContent(null, null, null);

        content = content.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = content.Split('\n');
        var sections = new Dictionary<string, StringBuilder>();
        string? currentSection = null;
        var inCodeFence = false;

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```"))
            {
                inCodeFence = !inCodeFence;
                if (currentSection != null)
                    sections[currentSection].Append(line).Append('\n');
                continue;
            }

            if (!inCodeFence && line.StartsWith("## "))
            {
                if (RecognizedHeadings.Contains(line))
                {
                    if (sections.ContainsKey(line))
                    {
                        Console.Error.WriteLine($"[WARN] Sidecar: duplicate '{line}' heading, ignoring");
                        currentSection = null;
                    }
                    else
                    {
                        currentSection = line;
                        sections[currentSection] = new StringBuilder();
                    }
                }
                else
                {
                    Console.Error.WriteLine($"[WARN] Sidecar: unknown heading '{line}' (recognized: ## Examples, ## Notes, ## See Also)");
                    currentSection = null;
                }
                continue;
            }

            if (currentSection != null)
                sections[currentSection].Append(line).Append('\n');
        }

        return new SidecarContent(
            Examples: GetTrimmed(sections, "## Examples"),
            Notes: GetTrimmed(sections, "## Notes"),
            SeeAlso: GetTrimmed(sections, "## See Also")
        );
    }

    private static string? GetTrimmed(Dictionary<string, StringBuilder> sections, string key)
    {
        if (!sections.TryGetValue(key, out var sb)) return null;
        var text = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
