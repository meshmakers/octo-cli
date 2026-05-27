namespace Meshmakers.Octo.Frontend.CommandReferenceGenerator;

public sealed record CommandDescriptor(
    string? Group,
    string Verb,
    string Description,
    IReadOnlyList<ArgumentDescriptor> Args)
{
    public string? ClassName { get; init; }

    public string? ExamplesMarkdown { get; init; }
    public string? NotesMarkdown { get; init; }
    public string? SeeAlsoMarkdown { get; init; }
}
