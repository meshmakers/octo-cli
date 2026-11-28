namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

/// <summary>
///     The tenant ids under which the platform's system tenant is addressed (<c>octosystem</c>, and <c>system</c> as
///     the short form some contexts use).
/// </summary>
internal static class SystemTenants
{
    private static readonly string[] Names = ["octosystem", "system"];

    /// <summary>True when <paramref name="tenantId" /> names the system tenant (case and surrounding blanks ignored).</summary>
    public static bool IsSystemTenant(string? tenantId) =>
        !string.IsNullOrWhiteSpace(tenantId) &&
        Names.Contains(tenantId.Trim().ToLowerInvariant());
}
