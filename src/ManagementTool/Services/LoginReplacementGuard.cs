namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

/// <summary>
///     Decides whether a non-interactive login may write its token into the effective context (AB#9063).
///     <para>
///         Context names are matched case-insensitively, so <c>--context local_octosystem</c> selects a stored
///         context <c>local_OctoSystem</c>. Reading through such a match is harmless; replacing that context's
///         login is not: a client-credentials token overwrote an interactive (device-code) login without a
///         word, and the human had to log in again. The guard refuses, naming the matched context, when
///     </para>
///     <list type="bullet">
///         <item>the requested name matched the stored context only by case, or</item>
///         <item>the context holds an interactive login (it carries a refresh token, which a
///             client-credentials login never has),</item>
///     </list>
///     <para>unless the caller passed the explicit replace flag.</para>
/// </summary>
internal static class LoginReplacementGuard
{
    /// <summary>
    ///     Returns the refusal message, or null when the login may replace the context's stored login.
    /// </summary>
    /// <param name="contextManager">The context manager of this invocation (context already selected).</param>
    /// <param name="replaceFlag">The flag that overrides the refusal, as the user types it (e.g. <c>--replace-login</c>).</param>
    public static string? GetRefusal(IContextManager contextManager, string replaceFlag)
    {
        var storedName = contextManager.GetEffectiveContextName();
        if (string.IsNullOrEmpty(storedName))
        {
            // No context to write into; the command fails on the missing tenant anyway.
            return null;
        }

        var requestedName = contextManager.RequestedContextName;
        var caseOnlyMatch = !string.IsNullOrEmpty(requestedName)
                            && !string.Equals(requestedName, storedName, StringComparison.Ordinal);

        var holdsInteractiveLogin =
            !string.IsNullOrEmpty(contextManager.GetEffectiveContext()?.Authentication.RefreshToken);

        if (!caseOnlyMatch && !holdsInteractiveLogin)
        {
            return null;
        }

        var reasons = new List<string>();
        if (caseOnlyMatch)
        {
            reasons.Add(
                $"The requested context '{requestedName}' matched the existing context '{storedName}' only by case " +
                "(context names are matched case-insensitively).");
        }

        if (holdsInteractiveLogin)
        {
            reasons.Add(
                $"Context '{storedName}' holds an interactive login; a client-credentials login would replace it, " +
                "and you would have to run LogIn again to get it back.");
        }

        return string.Join(" ", reasons) +
               $" Nothing was changed. Pass {replaceFlag} to replace the login of '{storedName}' anyway, " +
               "or add a separate context for the client (octo-cli -c AddContext -n <name> ...) and log in there.";
    }
}
