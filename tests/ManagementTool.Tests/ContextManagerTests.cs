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
}
