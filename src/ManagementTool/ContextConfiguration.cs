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

    [JsonPropertyName("Contexts")]
    public Dictionary<string, ContextEntry> Contexts { get; set; } = new();
}
