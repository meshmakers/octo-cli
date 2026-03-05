namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

public interface IContextManager
{
    ContextConfiguration Load();

    void Save(ContextConfiguration configuration);

    ContextEntry? GetActiveContext();

    string? GetActiveContextName();

    void AddOrUpdateContext(string name, ContextEntry entry);

    void RemoveContext(string name);

    void SetActiveContext(string name);

    IReadOnlyDictionary<string, ContextEntry> ListContexts();

    void MigrateIfNeeded();

    void SaveActiveContext();
}
