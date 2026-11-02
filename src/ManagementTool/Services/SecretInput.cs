using Meshmakers.Common.CommandLineParser;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

/// <summary>
///     Describes one secret a command reads (a password or a client secret, AB#8044).
/// </summary>
/// <param name="Name">What the secret is, for prompts and messages ("password", "client secret").</param>
/// <param name="LegacyArgument">
///     The deprecated argument that takes the value on the command line (<c>-p</c>, <c>-s</c>, <c>-cs</c>). It still
///     works in this release but logs a deprecation warning: the value lands in the shell history and the process list.
/// </param>
/// <param name="StdinArgument">The flag that reads the value from standard input (<c>--password-stdin</c>).</param>
/// <param name="EnvironmentVariable">The environment variable for automation.</param>
/// <param name="Required">When true, a missing value is an error; when false, the command runs without it.</param>
/// <param name="ConfirmPrompt">When true, the hidden prompt asks twice (a new secret that nobody can see while typing).</param>
public sealed record SecretSpec(
    string Name,
    IArgument LegacyArgument,
    IArgument StdinArgument,
    string EnvironmentVariable,
    bool Required,
    bool ConfirmPrompt);

/// <summary>
///     Reads a password or secret without putting it on the command line (AB#8044). Sources, first match wins:
///     <list type="number">
///         <item>the deprecated command-line argument (warning logged, never the value);</item>
///         <item>standard input, when the stdin flag is given (one value, a trailing line break is removed);</item>
///         <item>the environment variable;</item>
///         <item>a hidden prompt on the terminal - only for a required secret and only when standard input is a terminal.</item>
///     </list>
///     The command-line argument and the stdin flag together are refused. A required secret that none of the sources
///     provides is an error naming the stdin flag and the environment variable.
/// </summary>
public sealed class SecretInput
{
    private readonly ISecretConsole _console;

    /// <summary>Creates the reader over the given console.</summary>
    public SecretInput(ISecretConsole console)
    {
        _console = console;
    }

    /// <summary>
    ///     Resolves the secret described by <paramref name="spec" /> from the parsed arguments.
    /// </summary>
    /// <returns>The secret, or null when it is not required and no source provides it.</returns>
    /// <exception cref="ToolException">Both the argument and the stdin flag are given, the input is empty, or a required secret is missing.</exception>
    public string? Resolve(ICommandArgumentValue arguments, SecretSpec spec, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(logger);

        var legacyUsed = arguments.IsArgumentUsed(spec.LegacyArgument);
        var stdinUsed = arguments.IsArgumentUsed(spec.StdinArgument);
        if (legacyUsed && stdinUsed)
        {
            throw new ToolException(
                $"Use either -{spec.LegacyArgument.ShortTerm} or --{spec.StdinArgument.LongTerm} for the {spec.Name}, not both.");
        }

        if (legacyUsed)
        {
            logger.LogWarning(
                "-{ShortTerm} (--{LongTerm}) is deprecated: the {Name} ends up in the shell history and the process list. "
                + "Omit it to be prompted, or use --{StdinArgument} or the environment variable {EnvironmentVariable}. "
                + "The argument will be removed in a later release.",
                spec.LegacyArgument.ShortTerm, spec.LegacyArgument.LongTerm, spec.Name, spec.StdinArgument.LongTerm,
                spec.EnvironmentVariable);
            return arguments.GetArgumentScalarValue<string>(spec.LegacyArgument);
        }

        if (stdinUsed)
        {
            var value = TrimLineBreak(_console.ReadInputToEnd());
            if (string.IsNullOrEmpty(value))
            {
                throw new ToolException($"--{spec.StdinArgument.LongTerm} was given, but standard input holds no {spec.Name}.");
            }

            return value;
        }

        var environmentValue = _console.GetEnvironmentVariable(spec.EnvironmentVariable);
        if (!string.IsNullOrEmpty(environmentValue))
        {
            return environmentValue;
        }

        if (!spec.Required)
        {
            return null;
        }

        if (_console.IsInputRedirected)
        {
            throw new ToolException(
                $"The {spec.Name} is required. Standard input is not a terminal, so it cannot be prompted for: "
                + $"use --{spec.StdinArgument.LongTerm} or the environment variable {spec.EnvironmentVariable}.");
        }

        return Prompt(spec);
    }

    private string Prompt(SecretSpec spec)
    {
        var value = _console.ReadHidden($"{Capitalize(spec.Name)}: ");
        if (string.IsNullOrEmpty(value))
        {
            throw new ToolException($"No {spec.Name} entered.");
        }

        if (spec.ConfirmPrompt && _console.ReadHidden($"Repeat {spec.Name}: ") != value)
        {
            throw new ToolException($"The two {spec.Name} entries do not match.");
        }

        return value;
    }

    private static string TrimLineBreak(string value)
    {
        if (value.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return value[..^2];
        }

        return value.EndsWith('\n') ? value[..^1] : value;
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
