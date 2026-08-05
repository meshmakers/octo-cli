using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Services;

namespace ManagementTool.Tests;

/// <summary>
/// Verifies that context names are matched case-insensitively, including across a
/// save/Load round-trip — the scenario that originally failed because System.Text.Json
/// rehydrates the Contexts dictionary with the ordinal (case-sensitive) comparer.
/// Each test runs against a throwaway directory so the developer's real ~/.octo-cli
/// is never touched.
/// </summary>
public sealed class ContextManagerTests : IDisposable
{
    private readonly string _baseDirectory;

    public ContextManagerTests()
    {
        _baseDirectory = Path.Combine(Path.GetTempPath(), "octo-cli-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_baseDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_baseDirectory))
        {
            Directory.Delete(_baseDirectory, recursive: true);
        }
    }

    private ContextManager NewManager() => new(_baseDirectory);

    private static ContextEntry SampleEntry(string tenant) =>
        new() { OctoToolOptions = new OctoToolOptions { TenantId = tenant } };

    [Fact]
    public void SetActiveContext_IsCaseInsensitive()
    {
        var sut = NewManager();
        sut.AddOrUpdateContext("local_octosystem", SampleEntry("octosystem"));

        sut.SetActiveContext("local_OctoSystem");

        // Persisted name uses the stored key's canonical casing, not the typed casing.
        Assert.Equal("local_octosystem", sut.GetActiveContextName());
    }

    [Fact]
    public void SetActiveContext_AfterReload_IsCaseInsensitive()
    {
        // Reproduces the original bug: write the file, then load it in a fresh manager
        // (which deserializes the dictionary with the ordinal comparer) and look it up
        // with a different casing.
        var writer = NewManager();
        writer.AddOrUpdateContext("local_octosystem", SampleEntry("octosystem"));

        var reader = NewManager();
        reader.Load();

        reader.SetActiveContext("LOCAL_OCTOSYSTEM");

        Assert.Equal("local_octosystem", reader.GetActiveContextName());
        Assert.Equal("octosystem", reader.GetActiveContext()!.OctoToolOptions.TenantId);
    }

    [Fact]
    public void GetActiveContext_ResolvesEntryRegardlessOfStoredCasing()
    {
        var writer = NewManager();
        writer.AddOrUpdateContext("Local_Voestalpine", SampleEntry("voestalpine"));

        var reader = NewManager();
        reader.Load();

        var active = reader.GetActiveContext();

        Assert.NotNull(active);
        Assert.Equal("voestalpine", active!.OctoToolOptions.TenantId);
    }

    [Fact]
    public void RemoveContext_IsCaseInsensitive()
    {
        var sut = NewManager();
        sut.AddOrUpdateContext("local_octosystem", SampleEntry("octosystem"));

        sut.RemoveContext("LOCAL_OctoSystem");

        Assert.Empty(sut.ListContexts());
    }

    [Fact]
    public void AddOrUpdateContext_CaseVariantOverwritesSameContext()
    {
        var sut = NewManager();
        sut.AddOrUpdateContext("local_octosystem", SampleEntry("octosystem"));

        // A name differing only in case is treated as the same context, not a second one.
        sut.AddOrUpdateContext("Local_OctoSystem", SampleEntry("changed"));

        var (name, entry) = Assert.Single(sut.ListContexts());
        Assert.Equal("local_octosystem", name);
        Assert.Equal("changed", entry.OctoToolOptions.TenantId);
    }

    [Fact]
    public void SetActiveContext_UnknownContext_Throws()
    {
        var sut = NewManager();
        sut.AddOrUpdateContext("local_octosystem", SampleEntry("octosystem"));

        Assert.Throws<ToolException>(() => sut.SetActiveContext("does_not_exist"));
    }

    [Fact]
    public void SelectContext_ChangesEffectiveContextButNotTheActiveOne()
    {
        var sut = NewManager();
        sut.AddOrUpdateContext("dev", SampleEntry("devtenant"));
        sut.AddOrUpdateContext("prod", SampleEntry("prodtenant"));

        sut.SelectContext("prod");

        // The whole point of the override: the active context is untouched, so a parallel
        // invocation using the active context is unaffected.
        Assert.Equal("dev", sut.GetActiveContextName());
        Assert.Equal("devtenant", sut.GetActiveContext()!.OctoToolOptions.TenantId);
        Assert.Equal("prod", sut.GetEffectiveContextName());
        Assert.Equal("prodtenant", sut.GetEffectiveContext()!.OctoToolOptions.TenantId);
        Assert.True(sut.IsContextOverridden);
    }

    [Fact]
    public void SelectContext_DoesNotWriteToDisk()
    {
        var writer = NewManager();
        writer.AddOrUpdateContext("dev", SampleEntry("devtenant"));
        writer.AddOrUpdateContext("prod", SampleEntry("prodtenant"));

        writer.SelectContext("prod");

        var reader = NewManager();
        reader.Load();
        Assert.Equal("dev", reader.GetActiveContextName());
    }

    [Fact]
    public void SelectContext_IsCaseInsensitiveAndCanonicalisesTheName()
    {
        var sut = NewManager();
        sut.AddOrUpdateContext("local_octosystem", SampleEntry("octosystem"));

        sut.SelectContext("LOCAL_OctoSystem");

        Assert.Equal("local_octosystem", sut.GetEffectiveContextName());
    }

    [Fact]
    public void SelectContext_UnknownContext_ThrowsAndNamesTheKnownOnes()
    {
        var sut = NewManager();
        sut.AddOrUpdateContext("dev", SampleEntry("devtenant"));
        sut.AddOrUpdateContext("prod", SampleEntry("prodtenant"));

        var exception = Assert.Throws<ToolException>(() => sut.SelectContext("stage"));

        Assert.Contains("stage", exception.Message);
        Assert.Contains("dev", exception.Message);
        Assert.Contains("prod", exception.Message);
    }

    [Fact]
    public void WithoutSelection_EffectiveContextIsTheActiveOne()
    {
        var sut = NewManager();
        sut.AddOrUpdateContext("dev", SampleEntry("devtenant"));

        Assert.False(sut.IsContextOverridden);
        Assert.Equal(sut.GetActiveContextName(), sut.GetEffectiveContextName());
        Assert.Same(sut.GetActiveContext(), sut.GetEffectiveContext());
    }

    [Fact]
    public void SaveEffectiveContext_WritesTheSelectedContext()
    {
        var writer = NewManager();
        writer.AddOrUpdateContext("dev", SampleEntry("devtenant"));
        writer.AddOrUpdateContext("prod", SampleEntry("prodtenant"));
        writer.SelectContext("prod");

        writer.GetEffectiveContext()!.Authentication.AccessToken = "prod-token";
        writer.SaveEffectiveContext();

        var reader = NewManager();
        reader.Load();
        Assert.Equal("prod-token", reader.ListContexts()["prod"].Authentication.AccessToken);
        Assert.Null(reader.ListContexts()["dev"].Authentication.AccessToken);
        Assert.Equal("dev", reader.GetActiveContextName());
    }

    [Fact]
    public void SaveEffectiveContext_KeepsChangesMadeByAnotherInstanceMeanwhile()
    {
        // Two parallel octo-cli processes, each with its own --context, both refreshing their
        // token. The write path re-reads and merges, so neither loses the other's token — a plain
        // "serialise my whole in-memory state" write would drop one of them.
        var setup = NewManager();
        setup.AddOrUpdateContext("dev", SampleEntry("devtenant"));
        setup.AddOrUpdateContext("prod", SampleEntry("prodtenant"));

        var devProcess = NewManager();
        devProcess.Load();
        devProcess.SelectContext("dev");

        var prodProcess = NewManager();
        prodProcess.Load();
        prodProcess.SelectContext("prod");

        devProcess.GetEffectiveContext()!.Authentication.AccessToken = "dev-token";
        devProcess.SaveEffectiveContext();

        prodProcess.GetEffectiveContext()!.Authentication.AccessToken = "prod-token";
        prodProcess.SaveEffectiveContext();

        var reader = NewManager();
        reader.Load();
        Assert.Equal("dev-token", reader.ListContexts()["dev"].Authentication.AccessToken);
        Assert.Equal("prod-token", reader.ListContexts()["prod"].Authentication.AccessToken);
    }

    [Fact]
    public void AddOrUpdateContext_KeepsContextsAddedByAnotherInstanceMeanwhile()
    {
        var first = NewManager();
        first.AddOrUpdateContext("dev", SampleEntry("devtenant"));

        // A second instance that loaded before "dev" existed must not wipe it out.
        var second = NewManager();
        second.Load();
        second.AddOrUpdateContext("prod", SampleEntry("prodtenant"));

        var reader = NewManager();
        reader.Load();
        Assert.Equal(2, reader.ListContexts().Count);
        Assert.Contains("dev", reader.ListContexts().Keys);
        Assert.Contains("prod", reader.ListContexts().Keys);
    }

    [Fact]
    public void ConcurrentSaves_LeaveTheFileValidAndCompletelyWritten()
    {
        var setup = NewManager();
        var names = Enumerable.Range(0, 8).Select(i => $"ctx{i}").ToList();
        foreach (var name in names)
        {
            setup.AddOrUpdateContext(name, SampleEntry($"tenant{name}"));
        }

        Parallel.ForEach(names, name =>
        {
            var manager = NewManager();
            manager.Load();
            manager.SelectContext(name);
            manager.GetEffectiveContext()!.Authentication.AccessToken = $"token-{name}";
            manager.SaveEffectiveContext();
        });

        var reader = NewManager();
        reader.Load();

        // No torn file, and every writer's token survived.
        Assert.Equal(names.Count, reader.ListContexts().Count);
        foreach (var name in names)
        {
            Assert.Equal($"token-{name}", reader.ListContexts()[name].Authentication.AccessToken);
        }
    }
}
