using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Knowledge;

/// <summary>
///     The content of a System.Ai <c>KnowledgePage</c> as the GraphQL API returns it, and the content hash the knowledge
///     tools of octo-mcp-service store with every page and version (AB#8565). The hash is SHA-256 (lower-case hex) over
///     the page rendered as markdown with version 0; <see cref="Render" /> is a faithful port of
///     <c>KnowledgePages.Render</c> in octo-mcp-service (<c>Services/Knowledge/KnowledgePages.cs</c>). Both repositories
///     pin the same golden hash in a test, so a change on one side breaks the other's test.
/// </summary>
internal sealed record KnowledgePageContent(
    string RtId,
    string Slug,
    string Title,
    string Kind,
    string? Summary,
    string Status,
    int CurrentVersion,
    IReadOnlyList<JsonObject> Sections,
    IReadOnlyList<JsonObject> Citations,
    IReadOnlyList<JsonObject> HallucinationFlags)
{
    /// <summary>The enum names of <c>System.Ai/VerificationStatus</c> in CK spelling.</summary>
    public static readonly string[] StatusNames = ["Draft", "InReview", "Verified", "Rejected"];

    /// <summary>Reads a page item of <c>systemAiKnowledgePage { items { … } }</c>.</summary>
    public static KnowledgePageContent FromGraphQl(JsonElement item)
    {
        return new KnowledgePageContent(
            Str(item, "rtId") ?? string.Empty,
            Str(item, "rtWellKnownName") ?? string.Empty,
            Str(item, "title") ?? string.Empty,
            Str(item, "kind") ?? "CONCEPT",
            Str(item, "summary"),
            Str(item, "verificationStatus") ?? "DRAFT",
            item.TryGetProperty("currentVersion", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0,
            Records(item, "sections"),
            Records(item, "citations"),
            Records(item, "hallucinationFlags"));
    }

    /// <summary>CONSTANT_CASE (GraphQL enum value) to PascalCase (CK enum name), e.g. <c>IN_REVIEW</c> to <c>InReview</c>.</summary>
    public static string ToPascalCase(string constantCase) => string.Concat(constantCase
        .Split('_', StringSplitOptions.RemoveEmptyEntries)
        .Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));

    /// <summary>PascalCase (CK enum name) to CONSTANT_CASE (GraphQL enum value), e.g. <c>InReview</c> to <c>IN_REVIEW</c>.</summary>
    public static string ToConstantCase(string pascalCase)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < pascalCase.Length; i++)
        {
            if (i > 0 && char.IsUpper(pascalCase[i]))
            {
                sb.Append('_');
            }

            sb.Append(char.ToUpperInvariant(pascalCase[i]));
        }

        return sb.ToString();
    }

    /// <summary>The content hash with <paramref name="status" /> (GraphQL enum value) as the verification status.</summary>
    public string HashWithStatus(string status) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Render(status))));

    /// <summary>The markdown of the page with version 0 and <paramref name="status" /> (port of KnowledgePages.Render).</summary>
    internal string Render(string status)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(Title).Append('\n').Append('\n');
        sb.Append("> slug `").Append(Slug).Append("` · ").Append(ToPascalCase(Kind)).Append(" · ")
            .Append(ToPascalCase(status)).Append(" · version ").Append(0.ToString(CultureInfo.InvariantCulture))
            .Append('\n').Append('\n');
        if (!string.IsNullOrWhiteSpace(Summary))
        {
            sb.Append(Summary).Append('\n').Append('\n');
        }

        foreach (var section in Sections.OrderBy(s => Int(s, "sortOrder")))
        {
            sb.Append("## ").Append(Text(section, "heading")).Append('\n').Append('\n')
                .Append(Text(section, "sectionContent")).Append('\n').Append('\n');
        }

        if (Citations.Count > 0)
        {
            sb.Append("## Citations").Append('\n').Append('\n');
            foreach (var c in Citations)
            {
                sb.Append("- [").Append(Text(c, "sectionRef")).Append("] \"").Append(Text(c, "sourceQuote")).Append('"');
                var sourceRtId = Text(c, "sourceRtId");
                if (!string.IsNullOrWhiteSpace(sourceRtId))
                {
                    sb.Append(" (source ").Append(sourceRtId);
                    var anchor = Text(c, "sourceAnchor");
                    if (!string.IsNullOrWhiteSpace(anchor))
                    {
                        sb.Append(", ").Append(anchor);
                    }

                    sb.Append(')');
                }

                sb.Append('\n');
            }

            sb.Append('\n');
        }

        if (HallucinationFlags.Count > 0)
        {
            sb.Append("## Unsupported claims").Append('\n').Append('\n');
            foreach (var f in HallucinationFlags)
            {
                sb.Append("- ").Append(Text(f, "claim")).Append(" (").Append(Text(f, "reason")).Append(')').Append('\n');
            }
        }

        return sb.ToString().TrimEnd() + "\n";
    }

    private static string? Str(JsonElement item, string name) =>
        item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static List<JsonObject> Records(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return v.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.Object)
            .Select(e =>
            {
                // Keep only set values: the knowledge tools store records without null attributes.
                var obj = new JsonObject();
                foreach (var p in e.EnumerateObject().Where(p => p.Value.ValueKind != JsonValueKind.Null))
                {
                    obj[p.Name] = JsonNode.Parse(p.Value.GetRawText());
                }

                return obj;
            })
            .ToList();
    }

    private static string Text(JsonObject record, string name) =>
        record[name] is JsonValue value ? value.ToString() : string.Empty;

    private static int Int(JsonObject record, string name) =>
        record[name] is JsonValue value && value.TryGetValue<int>(out var n) ? n : 0;
}
