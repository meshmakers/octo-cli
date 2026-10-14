using System.IO.Compression;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Services;

namespace ManagementTool.Tests;

public sealed class CkModelFileInspectorTests : IDisposable
{
    private const string MetaOnlyYaml =
        "$schema: https://schemas.meshmakers.cloud/construction-kit-meta.schema.json\ndependencies:\n- Basic\nmodelId: AccountStatement\n";

    private const string CompiledYaml =
        "$schema: https://schemas.meshmakers.cloud/construction-kit-compiled.schema.json\ndependencies:\n- System-2.5.0\ntypes:\n- typeId: City\n  derivedFromCkTypeId: Basic-2.4.0/TreeNode-1\nmodelId: Basic-2.4.0\n";

    private readonly string _dir = Directory.CreateTempSubdirectory("ckinspector").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Theory]
    [InlineData(MetaOnlyYaml)]
    [InlineData("modelId: X\ntypes: []\nrecords: {}\nenums:\nattributes: # none\n")]
    [InlineData("modelId: X\ntypes:\n# only a comment\nrecords:\nmodelId2: y\n")]
    public void IsEmptyYaml_True_WhenNoContentKeyHasAnEntry(string yaml) =>
        Assert.True(CkModelFileInspector.IsEmptyYaml(yaml));

    [Theory]
    [InlineData(CompiledYaml)]
    [InlineData("modelId: X\nenums:\n  - enumId: E\n")]
    [InlineData("modelId: X\nrecords:\n- recordId: R\n")]
    [InlineData("modelId: X\nattributes:\n- attributeId: A\n")]
    [InlineData("modelId: X\nassociationRoles:\n- roleId: R\n")]
    [InlineData("modelId: X\ninterfaces:\n- interfaceId: I\n")]
    [InlineData("modelId: X\ntypes: [a]\n")]
    public void IsEmptyYaml_False_WhenAContentKeyHasAnEntry(string yaml) =>
        Assert.False(CkModelFileInspector.IsEmptyYaml(yaml));

    [Fact]
    public void IsEmptyYaml_False_ForContentWithoutAnyKey() =>
        Assert.False(CkModelFileInspector.IsEmptyYaml(""));

    [Theory]
    [InlineData("{\"modelId\":\"X\",\"dependencies\":[\"Basic\"]}", true)]
    [InlineData("{\"modelId\":\"X\",\"types\":[],\"enums\":{}}", true)]
    [InlineData("{\"modelId\":\"X\",\"types\":[{\"typeId\":\"T\"}]}", false)]
    [InlineData("{\"modelId\":\"X\",\"Enums\":[{\"enumId\":\"E\"}]}", false)]
    [InlineData("not json", false)]
    public void IsEmptyJson_Classifies(string json, bool expected) =>
        Assert.Equal(expected, CkModelFileInspector.IsEmptyJson(json));

    [Fact]
    public void EnsureNotEmpty_Throws_ForMetaOnlyYamlFile()
    {
        var path = Write("empty.yaml", MetaOnlyYaml);

        var exception = Assert.Throws<ToolException>(() => CkModelFileInspector.EnsureNotEmpty(path));

        Assert.Contains("is empty", exception.Message);
        Assert.Contains(path, exception.Message);
    }

    [Fact]
    public void EnsureNotEmpty_Passes_ForCompiledYamlFile() =>
        CkModelFileInspector.EnsureNotEmpty(Write("model.yaml", CompiledYaml));

    [Fact]
    public void EnsureNotEmpty_Throws_ForEmptyJsonFile() =>
        Assert.Throws<ToolException>(() =>
            CkModelFileInspector.EnsureNotEmpty(Write("empty.json", "{\"modelId\":\"X\"}")));

    [Fact]
    public void EnsureNotEmpty_Passes_ForUnknownExtension() =>
        CkModelFileInspector.EnsureNotEmpty(Write("model.txt", MetaOnlyYaml));

    [Fact]
    public void EnsureNotEmpty_Throws_ForZipWhereEveryModelIsEmpty()
    {
        var path = ZipOf(("a.yaml", MetaOnlyYaml), ("b.yaml", MetaOnlyYaml));

        Assert.Throws<ToolException>(() => CkModelFileInspector.EnsureNotEmpty(path));
    }

    [Fact]
    public void EnsureNotEmpty_Passes_ForZipWithOneNonEmptyModel() =>
        CkModelFileInspector.EnsureNotEmpty(ZipOf(("a.yaml", MetaOnlyYaml), ("b.yaml", CompiledYaml)));

    [Fact]
    public void EnsureNotEmpty_Passes_ForZipWithoutModelFiles() =>
        CkModelFileInspector.EnsureNotEmpty(ZipOf(("readme.txt", "hello")));

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string ZipOf(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(_dir, $"{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(content);
        }

        return path;
    }
}
