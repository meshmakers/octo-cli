using GraphQlDtos.DataTransferObjects.System.Identity.v3;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;

/// <summary>
///     The table of a <c>Client.SetMirrorRoles-1</c> result: one line per tracked mirror (child tenant) with its status,
///     the direct roles of the mirror before and after the change. Pure, so the table is tested without a service.
///     <list type="bullet">
///         <item>
///             <b>Roles before</b> = already present + removed + hand-set; <b>roles after</b> = already present + added +
///             hand-set (hand-set are the roles of the mirror the list does not name and identity does not manage; they
///             are never removed). Roles the tenant declined, refused or missing roles are not on the mirror before or after.
///             <c>?</c> = the result carries no role data for that mirror (<see cref="RolesRead" />).
///         </item>
///         <item>
///             A dry run names what would happen (<c>WOULD-ADD</c>, <c>WOULD-REMOVE</c>, <c>WOULD-ADOPT</c>), a real run what
///             happened (<c>ADDED</c>, <c>REMOVED</c>, <c>ADOPTED</c>); the other markers are the same in both.
///         </item>
///     </list>
/// </summary>
internal static class ClientMirrorRolesTable
{
    internal const string FailedOutcome = "Failed";

    private static readonly string[] Header = ["Tenant", "Status", "Roles before", "Roles after"];

    /// <summary>The status markers of one mirror, in a fixed order.</summary>
    public static IReadOnlyList<string> StatusOf(RtClientMirrorRoleTenantResultRecordDto tenant, bool dryRun)
    {
        var status = new List<string>();
        if (string.Equals(tenant.Outcome, FailedOutcome, StringComparison.OrdinalIgnoreCase))
        {
            status.Add("FAILED");
        }
        else if (string.Equals(tenant.Outcome, "Skipped", StringComparison.OrdinalIgnoreCase))
        {
            status.Add("SKIPPED");
        }

        Add(status, tenant.Added, dryRun ? "WOULD-ADD" : "ADDED");
        Add(status, tenant.Removed, dryRun ? "WOULD-REMOVE" : "REMOVED");
        Add(status, tenant.Adopted, dryRun ? "WOULD-ADOPT" : "ADOPTED");
        Add(status, Except(tenant.AlreadyPresent, tenant.Adopted), "ALREADY-PRESENT");
        Add(status, tenant.HandSet, "HAND-SET");
        Add(status, tenant.RoleMissing, "ROLE-MISSING");
        Add(status, tenant.Declined, "DECLINED");
        Add(status, tenant.RoleRefused, "ROLE-REFUSED");
        if (status.Count == 0)
        {
            status.Add("NO-ROLES");
        }

        return status;
    }

    /// <summary>
    ///     Whether the result read the mirror's roles. A <c>Failed</c> or <c>Skipped</c> mirror and a mirror the server
    ///     answered with empty lists only (an empty list on a client with nothing tracked) carry no role data, so the table
    ///     prints <c>?</c> instead of claiming the mirror holds no roles.
    /// </summary>
    public static bool RolesRead(RtClientMirrorRoleTenantResultRecordDto tenant) =>
        !string.Equals(tenant.Outcome, FailedOutcome, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(tenant.Outcome, "Skipped", StringComparison.OrdinalIgnoreCase)
        && new[] { tenant.Added, tenant.Removed, tenant.AlreadyPresent, tenant.Adopted, tenant.HandSet, tenant.RoleMissing,
                tenant.Declined, tenant.RoleRefused }
            .Any(l => l?.Any(n => !string.IsNullOrWhiteSpace(n)) == true);

    /// <summary>The direct roles of the mirror before the change, sorted; <c>null</c> when they were not read.</summary>
    public static IReadOnlyList<string>? RolesBefore(RtClientMirrorRoleTenantResultRecordDto tenant) =>
        RolesRead(tenant) ? Union(tenant.AlreadyPresent, tenant.Removed, tenant.HandSet) : null;

    /// <summary>
    ///     The direct roles of the mirror after the change (a dry run: after an apply), sorted; <c>null</c> when they were
    ///     not read.
    /// </summary>
    public static IReadOnlyList<string>? RolesAfter(RtClientMirrorRoleTenantResultRecordDto tenant) =>
        RolesRead(tenant) ? Union(tenant.AlreadyPresent, tenant.Added, tenant.HandSet) : null;

    /// <summary>The lines of the table, header first, columns padded.</summary>
    public static IReadOnlyList<string> Format(RtClientMirrorRolesResultRecordDto result)
    {
        var dryRun = result.DryRun == true;
        var rows = (result.Tenants ?? [])
            .Where(t => t != null)
            .OrderBy(t => t!.ChildTenantId, StringComparer.OrdinalIgnoreCase)
            .Select(t => new[]
            {
                t!.ChildTenantId ?? "",
                string.Join(",", StatusOf(t, dryRun)),
                Join(RolesBefore(t)),
                Join(RolesAfter(t))
            })
            .Prepend(Header)
            .ToList();
        var widths = Enumerable.Range(0, Header.Length).Select(i => rows.Max(r => r[i].Length)).ToArray();
        return rows.Select(r => string.Join(" | ", r.Select((c, i) => c.PadRight(widths[i]))).TrimEnd()).ToList();
    }

    private static string Join(IReadOnlyList<string>? roles) =>
        roles == null ? "?" : roles.Count == 0 ? "-" : string.Join(",", roles);

    private static void Add(List<string> status, IEnumerable<string?>? names, string marker)
    {
        if (names?.Any(n => !string.IsNullOrWhiteSpace(n)) == true)
        {
            status.Add(marker);
        }
    }

    private static IEnumerable<string?> Except(IEnumerable<string?>? names, IEnumerable<string?>? without) =>
        (names ?? []).Except(without ?? [], StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<string> Union(params IEnumerable<string?>?[] lists) =>
        lists.SelectMany(l => l ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
