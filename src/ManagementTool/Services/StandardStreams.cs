namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

/// <inheritdoc />
public sealed class StandardStreams : IStandardStreams
{
    /// <inheritdoc />
    public bool IsInputRedirected => Console.IsInputRedirected;

    /// <inheritdoc />
    public Task<string> ReadInputToEndAsync(CancellationToken cancellationToken = default) =>
        Console.In.ReadToEndAsync(cancellationToken);

    /// <inheritdoc />
    public void WriteOutput(string text) => Write(Console.Out, text);

    /// <inheritdoc />
    public void WriteError(string text) => Write(Console.Error, text);

    private static void Write(TextWriter writer, string text)
    {
        writer.Write(text);
        if (!text.EndsWith('\n'))
        {
            writer.Write('\n');
        }

        writer.Flush();
    }
}
