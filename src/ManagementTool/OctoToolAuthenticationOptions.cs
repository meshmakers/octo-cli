using System;
using System.Text.Json.Serialization;

namespace Meshmakers.Octo.Frontend.ManagementTool;

public class OctoToolAuthenticationOptions
{
    public string? AccessToken { get; set; }

    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public DateTime? AccessTokenExpiresAt { get; set; }
    public string? RefreshToken { get; set; }

    /// <summary>
    ///     Tokens exchanged from this context's token for other tenants (AB#6836), per tenant id. Kept in
    ///     the context file next to the token they were exchanged from and protected the same way; a
    ///     token is re-exchanged silently when it is about to expire or the context's token changed.
    /// </summary>
    [JsonPropertyName("TenantTokens")]
    public Dictionary<string, TenantTokenEntry>? TenantTokens { get; set; }
}

public class TenantTokenEntry
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public string SubjectFingerprint { get; set; } = string.Empty;
}
