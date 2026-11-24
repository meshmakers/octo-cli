namespace Meshmakers.Octo.Frontend.ManagementTool;

public class OctoToolOptions
{
    public string? BotServiceUrl { get; set; }
    public string? AssetServiceUrl { get; set; }
    public string? IdentityServiceUrl { get; set; }
    public string? CommunicationServiceUrl { get; set; }
    public string? ReportingServiceUrl { get; set; }
    public string? AiServiceUrl { get; set; }

    /// <summary>Base URI of the MCP service (octo-mcp-service); the knowledge platform-update commands call it.</summary>
    public string? McpServiceUrl { get; set; }

    public string? TenantId { get; set; }
}
