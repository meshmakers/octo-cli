namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

/// <summary>
///     Scans the raw command line for the context argument, before the DI container exists.
///     <para>
///         The container is built before the command line is parsed: <see cref="OctoToolOptions" />
///         and every service-client options object are filled from the context at that point, and the
///         service clients themselves are constructed while the command list is enumerated. The
///         selected context therefore has to be known earlier than the parser can tell us, which is
///         what this pre-scan is for. The argument is additionally declared on the parser
///         (see <see cref="OctoCommandParser" />) so it is validated, appears in the usage output,
///         and a disagreement between both is reported instead of silently ignored.
///     </para>
/// </summary>
public static class ContextOverrideResolver
{
    /// <summary>
    ///     Reads the value of the first context argument on the command line. Returns null when the
    ///     argument is absent or has no value — in the latter case the parser reports the missing
    ///     value, so there is nothing to do here. The first occurrence wins, matching
    ///     <c>IArgumentValue.GetValue</c>, which reads index 0.
    /// </summary>
    /// <param name="commandLineArgs">
    ///     Raw command line including the executable name at index 0, as returned by
    ///     <c>Environment.GetCommandLineArgs()</c>. The first entry is skipped, mirroring
    ///     <c>ParserService.ParseAndValidate</c>.
    /// </param>
    public static string? ResolveFromCommandLine(IReadOnlyList<string> commandLineArgs)
    {
        for (var i = 1; i < commandLineArgs.Count; i++)
        {
            if (!IsContextTerm(commandLineArgs[i]))
            {
                continue;
            }

            if (i + 1 >= commandLineArgs.Count)
            {
                return null;
            }

            var value = commandLineArgs[i + 1];

            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        return null;
    }

    /// <summary>
    ///     Mirrors how the parser matches this argument. <c>Argument.Compare</c> compares at least
    ///     <c>ShortTerm.Length</c> characters, and short and long term are both "context", so only
    ///     the full term matches — abbreviations such as <c>--contex</c> do not, and neither does a
    ///     longer term such as <c>--contextfoo</c>.
    /// </summary>
    private static bool IsContextTerm(string term)
    {
        return term.Equals($"--{Constants.ContextArgumentTerm}", StringComparison.OrdinalIgnoreCase)
               || term.Equals($"-{Constants.ContextArgumentTerm}", StringComparison.OrdinalIgnoreCase)
               || term.Equals($"/{Constants.ContextArgumentTerm}", StringComparison.OrdinalIgnoreCase);
    }
}
