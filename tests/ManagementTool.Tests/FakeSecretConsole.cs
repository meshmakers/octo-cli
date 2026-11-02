using Meshmakers.Octo.Frontend.ManagementTool.Services;

namespace ManagementTool.Tests;

/// <summary>
///     AB#8044 — a scripted terminal for <see cref="SecretInput" />: standard input, hidden prompt answers and environment
///     variables are set per test, and nothing is read from the real process environment.
/// </summary>
internal sealed class FakeSecretConsole : ISecretConsole
{
    private readonly Queue<string?> _promptAnswers = new();

    public bool IsInputRedirected { get; set; }

    public string StandardInput { get; set; } = string.Empty;

    public Dictionary<string, string> Environment { get; } = new(StringComparer.Ordinal);

    public List<string> Prompts { get; } = [];

    public int StandardInputReads { get; private set; }

    public FakeSecretConsole Answer(params string?[] answers)
    {
        foreach (var answer in answers)
        {
            _promptAnswers.Enqueue(answer);
        }

        return this;
    }

    public string ReadInputToEnd()
    {
        StandardInputReads++;
        return StandardInput;
    }

    public string? ReadHidden(string prompt)
    {
        Prompts.Add(prompt);
        return _promptAnswers.Count > 0 ? _promptAnswers.Dequeue() : null;
    }

    public string? GetEnvironmentVariable(string name) => Environment.GetValueOrDefault(name);

    /// <summary>A <see cref="SecretInput" /> over a console without input, prompt answers or environment.</summary>
    public static SecretInput Empty() => new(new FakeSecretConsole { IsInputRedirected = true });
}
