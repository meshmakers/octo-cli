using System.IO.Compression;
using System.Text.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

/// <summary>
///     Client-side check of a construction kit file before <c>ImportCk</c> uploads it (AB#2615).
///     A CK model is empty when it declares none of the content elements (types, records, enums,
///     attributes, association roles, interfaces) — e.g. a file that only holds <c>modelId</c> and
///     <c>dependencies</c>. The file is read with a light top-level scan (no YAML dependency): only the
///     top-level keys and whether they carry any value are looked at; the server stays the authority for
///     everything else.
/// </summary>
internal static class CkModelFileInspector
{
    /// <summary>Top-level keys of a compiled CK model that make up its content.</summary>
    internal static readonly string[] ContentKeys =
        ["types", "records", "enums", "attributes", "associationRoles", "interfaces"];

    /// <summary>
    ///     Throws a <see cref="ToolException" /> when the file is a CK model without any content.
    ///     Files that cannot be classified (unreadable, unknown extension) pass; the upload reports those.
    /// </summary>
    public static void EnsureNotEmpty(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        switch (extension)
        {
            case ".yaml":
            case ".yml":
                if (IsEmptyYaml(File.ReadAllText(filePath)))
                {
                    throw ToolException.EmptyCkModel(filePath);
                }

                break;
            case ".json":
                if (IsEmptyJson(File.ReadAllText(filePath)))
                {
                    throw ToolException.EmptyCkModel(filePath);
                }

                break;
            case ".zip":
                EnsureZipNotEmpty(filePath);
                break;
        }
    }

    internal static bool IsEmptyYaml(string yaml)
    {
        var lines = yaml.Split('\n');
        var hasKey = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length == 0 || line[0] is ' ' or '\t' or '#' or '-')
            {
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            hasKey = true;
            var key = line[..colon].Trim().Trim('"', '\'');
            if (!ContentKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var inline = StripComment(line[(colon + 1)..]).Trim();
            if (inline.Length > 0)
            {
                if (inline is not ("[]" or "{}" or "null" or "~"))
                {
                    return false;
                }

                continue;
            }

            // Block value: any non-blank, non-comment line before the next top-level key (a top-level
            // "- item" belongs to the block, too).
            for (var j = i + 1; j < lines.Length; j++)
            {
                var next = lines[j].TrimEnd('\r');
                if (next.Trim().Length == 0 || next.TrimStart()[0] == '#')
                {
                    continue;
                }

                if (next[0] is ' ' or '\t' or '-')
                {
                    return false;
                }

                break;
            }
        }

        // A YAML file without any key is not a CK model we can judge: leave it to the server.
        return hasKey;
    }

    internal static bool IsEmptyJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json,
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!ContentKeys.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var hasContent = property.Value.ValueKind switch
                {
                    JsonValueKind.Array => property.Value.GetArrayLength() > 0,
                    JsonValueKind.Object => property.Value.EnumerateObject().Any(),
                    JsonValueKind.Null or JsonValueKind.Undefined => false,
                    _ => true
                };
                if (hasContent)
                {
                    return false;
                }
            }

            return true;
        }
        catch (JsonException)
        {
            return false; // not parseable: the server reports it
        }
    }

    private static void EnsureZipNotEmpty(string filePath)
    {
        using var archive = ZipFile.OpenRead(filePath);
        var checkedModels = 0;
        foreach (var entry in archive.Entries)
        {
            var extension = Path.GetExtension(entry.Name).ToLowerInvariant();
            if (extension is not (".yaml" or ".yml" or ".json"))
            {
                continue;
            }

            using var reader = new StreamReader(entry.Open());
            var content = reader.ReadToEnd();
            checkedModels++;
            var isEmpty = extension == ".json" ? IsEmptyJson(content) : IsEmptyYaml(content);
            if (!isEmpty)
            {
                return;
            }
        }

        if (checkedModels > 0)
        {
            throw ToolException.EmptyCkModel(filePath);
        }
    }

    private static string StripComment(string value)
    {
        var hash = value.IndexOf(" #", StringComparison.Ordinal);
        return hash >= 0 ? value[..hash] : value;
    }
}
