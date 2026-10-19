using NLog;
using NLog.Targets;

namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

/// <summary>
///     Keeps standard output free for the answer of commands that scripts pipe (AB#7027). The console log target
///     writes the banner, the context lines and every progress message to standard output; for these commands it is
///     switched to standard error, so a script that reads standard output gets nothing but the answer.
///     <para>
///         The decision has to be made before the first line is logged, i.e. from the raw command line like the
///         context selection (<see cref="ContextOverrideResolver" />).
///     </para>
/// </summary>
public static class MachineReadableOutput
{
    /// <summary>Names of the commands (the value of <c>-c</c>) whose standard output is machine readable.</summary>
    public static readonly IReadOnlyList<string> CommandNames = ["GraphQl"];

    private static readonly string[] CommandTerms = ["-c", "--c", "/c", "-command", "--command", "/command"];

    /// <summary>
    ///     True when the command line selects one of <see cref="CommandNames" />. The first entry (the executable
    ///     name, as returned by <c>Environment.GetCommandLineArgs()</c>) is skipped.
    /// </summary>
    public static bool IsRequested(IReadOnlyList<string> commandLineArgs)
    {
        for (var i = 1; i < commandLineArgs.Count - 1; i++)
        {
            if (CommandTerms.Contains(commandLineArgs[i], StringComparer.OrdinalIgnoreCase))
            {
                return CommandNames.Contains(commandLineArgs[i + 1].Trim(), StringComparer.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    /// <summary>
    ///     Switches every console log target to standard error. Safe to call more than once and before or after the
    ///     logging configuration has been replaced.
    /// </summary>
    public static void RouteLogsToStandardError()
    {
        var configuration = LogManager.Configuration;
        if (configuration == null)
        {
            return;
        }

        foreach (var target in configuration.AllTargets.OfType<ColoredConsoleTarget>())
        {
            target.StdErr = true;
        }

        foreach (var target in configuration.AllTargets.OfType<ConsoleTarget>())
        {
            target.StdErr = true;
        }

        LogManager.ReconfigExistingLoggers();
    }
}
