namespace Meshmakers.Octo.Frontend.CommandReferenceGenerator;

public sealed record ArgumentDescriptor(
    string Short,
    string Long,
    string Help,
    bool IsRequired,
    int ValueCount);
