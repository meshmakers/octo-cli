using System.Text.Json.Serialization;

namespace Meshmakers.Octo.Frontend.ManagementTool;

public class ContextEntry
{
    [JsonPropertyName("OctoToolOptions")]
    public OctoToolOptions OctoToolOptions { get; set; } = new();

    [JsonPropertyName("Authentication")]
    public OctoToolAuthenticationOptions Authentication { get; set; } = new();
}

public class ContextConfiguration
{
    [JsonPropertyName("ActiveContext")]
    public string? ActiveContext { get; set; }

    // Context names are matched case-insensitively. Note: System.Text.Json discards
    // this comparer on deserialize and uses the ordinal one, so ContextManager.Load
    // rebuilds the dictionary with OrdinalIgnoreCase again after reading the file.
    [JsonPropertyName("Contexts")]
    public Dictionary<string, ContextEntry> Contexts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
