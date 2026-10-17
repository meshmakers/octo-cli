using Meshmakers.Octo.Sdk.ServiceClient.Authentication;

namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

/// <summary>
///     Keeps exchanged tenant tokens in the entry of the effective context (AB#6836), so a sweep of several
///     octo-cli invocations exchanges once per tenant. The entry already holds the token they were
///     exchanged from; nothing new is written to a place that was not protected for it.
/// </summary>
public sealed class ContextTenantTokenStore(IContextManager contextManager) : ITenantTokenStore
{
    public CachedTenantToken? Get(string tenantId)
    {
        var tokens = contextManager.GetEffectiveContext()?.Authentication.TenantTokens;
        if (tokens == null || !tokens.TryGetValue(Key(tenantId), out var entry) || string.IsNullOrEmpty(entry.AccessToken))
        {
            return null;
        }

        return new CachedTenantToken(entry.AccessToken, DateTime.SpecifyKind(entry.ExpiresAtUtc, DateTimeKind.Utc),
            entry.SubjectFingerprint);
    }

    // Tenant ids are case-insensitive; the dictionary read from the context file is not, so the key is normalized.
    private static string Key(string tenantId) => tenantId.ToLowerInvariant();

    public void Set(string tenantId, CachedTenantToken token)
    {
        var context = contextManager.GetEffectiveContext();
        if (context == null)
        {
            return;
        }

        var tokens = context.Authentication.TenantTokens ??=
            new Dictionary<string, TenantTokenEntry>();
        tokens[Key(tenantId)] = new TenantTokenEntry
        {
            AccessToken = token.AccessToken,
            ExpiresAtUtc = token.ExpiresAtUtc,
            SubjectFingerprint = token.SubjectFingerprint
        };

        // Drop what has expired, so the file does not collect one token per tenant ever addressed.
        foreach (var expired in tokens.Where(t => t.Value.ExpiresAtUtc < DateTime.UtcNow).Select(t => t.Key).ToList())
        {
            tokens.Remove(expired);
        }

        contextManager.SaveEffectiveContext();
    }
}
