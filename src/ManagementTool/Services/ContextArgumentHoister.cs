namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

/// <summary>
///     Moves the <c>--context &lt;name&gt;</c> pairs of a command line in front of everything else, so the
///     argument works at any position.
///     <para>
///         The parser has one layer for the top-level arguments and one per command. An argument the
///         command layer does not know ends that layer, and the top layer takes over for the rest of the
///         line — it never returns to the command. <c>-c FixAll --context x -w</c> would therefore hand
///         <c>-w</c> to the top layer, which rejects it as unknown. Placing the context first keeps the
///         command layer uninterrupted. Everything else keeps its relative order.
///     </para>
/// </summary>
public static class ContextArgumentHoister
{
    /// <summary>
    ///     Returns the command line with every context argument and its value directly after the
    ///     executable name. A context term without a value (last on the line) stays where it is, so the
    ///     parser reports the missing value as before.
    /// </summary>
    /// <param name="commandLineArgs">
    ///     Raw command line including the executable name at index 0, as returned by
    ///     <c>Environment.GetCommandLineArgs()</c>.
    /// </param>
    public static string[] Hoist(IReadOnlyList<string> commandLineArgs)
    {
        if (commandLineArgs.Count == 0)
        {
            return [];
        }

        var hoisted = new List<string>();
        var rest = new List<string>();

        for (var i = 1; i < commandLineArgs.Count; i++)
        {
            if (ContextOverrideResolver.IsContextTerm(commandLineArgs[i]) && i + 1 < commandLineArgs.Count)
            {
                hoisted.Add(commandLineArgs[i]);
                hoisted.Add(commandLineArgs[i + 1]);
                i++;
                continue;
            }

            rest.Add(commandLineArgs[i]);
        }

        return [commandLineArgs[0], .. hoisted, .. rest];
    }
}
