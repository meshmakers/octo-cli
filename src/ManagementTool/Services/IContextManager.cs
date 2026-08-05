namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

public interface IContextManager
{
    /// <summary>
    ///     Path of the file the contexts are read from and written to.
    /// </summary>
    string ConfigurationFilePath { get; }

    /// <summary>
    ///     True when a context has been selected for this invocation only, so the effective
    ///     context differs from the persisted active one.
    /// </summary>
    bool IsContextOverridden { get; }

    ContextConfiguration Load();

    void Save(ContextConfiguration configuration);

    /// <summary>
    ///     The context persisted as active. Use this only where the persisted selection itself is
    ///     the subject (listing contexts, switching them); everything acting on "the context in
    ///     use" wants <see cref="GetEffectiveContext" />.
    /// </summary>
    ContextEntry? GetActiveContext();

    string? GetActiveContextName();

    /// <summary>
    ///     The context this invocation runs against: the one selected via
    ///     <see cref="SelectContext" />, or the persisted active one.
    /// </summary>
    ContextEntry? GetEffectiveContext();

    string? GetEffectiveContextName();

    /// <summary>
    ///     Selects a context for this invocation without persisting anything, so parallel
    ///     invocations can run against different contexts.
    /// </summary>
    /// <param name="name">Name of a stored context, matched case-insensitively.</param>
    /// <exception cref="ToolException">Thrown when no context of that name is stored.</exception>
    void SelectContext(string name);

    void AddOrUpdateContext(string name, ContextEntry entry);

    void RemoveContext(string name);

    void SetActiveContext(string name);

    IReadOnlyDictionary<string, ContextEntry> ListContexts();

    void MigrateIfNeeded();

    /// <summary>
    ///     Persists the effective context's entry, merging it onto the file's current content so a
    ///     parallel invocation working on another context does not get overwritten.
    /// </summary>
    void SaveEffectiveContext();
}
