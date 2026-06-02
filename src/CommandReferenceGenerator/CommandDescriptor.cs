namespace Meshmakers.Octo.Frontend.CommandReferenceGenerator;

public sealed record CommandDescriptor(
    string? Group,
    string Verb,
    string Description,
    IReadOnlyList<ArgumentDescriptor> Args)
{
    public string? ClassName { get; init; }

    public IReadOnlyList<SampleDescriptor>? Samples { get; init; }
    public IReadOnlyList<string>? Notes { get; init; }
}

public sealed record SampleDescriptor(
    IReadOnlyList<SampleArgumentBinding> Arguments,
    string Description,
    string? ExpectedOutput = null);

/// <summary>
///     A single (argument, value) pair inside a <see cref="SampleDescriptor"/>. The argument is resolved
///     to its <see cref="ArgumentDescriptor"/> at extraction time, so the renderer can use the current
///     <c>ShortName</c>/<c>LongName</c> without the sample itself naming them.
///     <see cref="Value"/> is null when the argument is a flag.
/// </summary>
public sealed record SampleArgumentBinding(ArgumentDescriptor Argument, string? Value);
