namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

/// <summary>
///     The process streams of commands whose output is consumed by scripts (AB#7027): the answer goes to standard
///     output and nothing else does, errors go to standard error, the query can come from standard input.
/// </summary>
public interface IStandardStreams
{
    /// <summary>True when standard input is a pipe or a file, not a terminal.</summary>
    bool IsInputRedirected { get; }

    /// <summary>Reads standard input to the end.</summary>
    Task<string> ReadInputToEndAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes <paramref name="text" /> to standard output, followed by a line break unless it ends with one.</summary>
    void WriteOutput(string text);

    /// <summary>Writes <paramref name="text" /> to standard error, followed by a line break unless it ends with one.</summary>
    void WriteError(string text);
}
