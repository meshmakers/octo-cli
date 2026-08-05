namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

/// <summary>
///     The context chosen for this invocation, resolved before the DI container is built.
///     Registered as a singleton so both the startup wiring and the parser see the same decision.
/// </summary>
/// <param name="Name">The requested context name, or null when the active context is used.</param>
/// <param name="CommandLineName">
///     The name found on the command line only. Kept apart from <paramref name="Name" /> so the
///     parser can verify that it agrees with the pre-scan.
/// </param>
/// <param name="Source">Where the name came from, for log output.</param>
public sealed record ContextSelection(string? Name, string? CommandLineName, string? Source)
{
    public static readonly ContextSelection None = new(null, null, null);

    public bool IsOverridden => Name != null;

    public static ContextSelection Resolve(IReadOnlyList<string> commandLineArgs,
        Func<string, string?> environmentVariableReader)
    {
        var fromCommandLine = ContextOverrideResolver.ResolveFromCommandLine(commandLineArgs);
        if (fromCommandLine != null)
        {
            return new ContextSelection(fromCommandLine, fromCommandLine, $"--{Constants.ContextArgumentTerm}");
        }

        var fromEnvironment = environmentVariableReader(Constants.EnvVarContext);

        return string.IsNullOrWhiteSpace(fromEnvironment)
            ? None
            : new ContextSelection(fromEnvironment.Trim(), null, Constants.EnvVarContext);
    }
}
