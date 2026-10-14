namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

/// <summary>
///     Generic helper to keep secret values (tokens, passwords, client secrets) out of console output,
///     logs and agent transcripts. Reveals only the length, never any part of the value.
/// </summary>
internal static class SecretMasker
{
    /// <summary>
    ///     Returns a placeholder for <paramref name="secret" />: <c>(not set)</c> for null/empty,
    ///     otherwise <c>********</c> plus the length.
    /// </summary>
    public static string Mask(string? secret) =>
        string.IsNullOrEmpty(secret) ? "(not set)" : $"******** ({secret.Length} chars)";
}
