using System.Text;

namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

/// <summary>
///     The console and environment access <see cref="SecretInput" /> needs (AB#8044), behind an interface so the
///     tests can drive the prompt, standard input and environment variables without a terminal.
/// </summary>
public interface ISecretConsole
{
    /// <summary>True when standard input is a pipe or a file, not a terminal: no prompt is possible.</summary>
    bool IsInputRedirected { get; }

    /// <summary>Reads standard input to its end.</summary>
    string ReadInputToEnd();

    /// <summary>
    ///     Writes <paramref name="prompt" /> to standard error and reads one line from the terminal without echoing
    ///     it. Returns null when the input ended.
    /// </summary>
    string? ReadHidden(string prompt);

    /// <summary>The value of the environment variable, or null.</summary>
    string? GetEnvironmentVariable(string name);
}

/// <inheritdoc />
public sealed class SecretConsole : ISecretConsole
{
    /// <inheritdoc />
    public bool IsInputRedirected => Console.IsInputRedirected;

    /// <inheritdoc />
    public string ReadInputToEnd() => Console.In.ReadToEnd();

    /// <inheritdoc />
    public string? ReadHidden(string prompt)
    {
        Console.Error.Write(prompt);
        Console.Error.Flush();
        var value = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    Console.Error.WriteLine();
                    return value.ToString();
                case ConsoleKey.Backspace:
                    if (value.Length > 0)
                    {
                        value.Length--;
                    }

                    break;
                default:
                    if (key.KeyChar == '\u0004' && value.Length == 0)
                    {
                        // Ctrl+D on an empty line: end of input
                        Console.Error.WriteLine();
                        return null;
                    }

                    if (!char.IsControl(key.KeyChar))
                    {
                        value.Append(key.KeyChar);
                    }

                    break;
            }
        }
    }

    /// <inheritdoc />
    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);
}
