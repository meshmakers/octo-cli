using FakeItEasy;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.PlatformFiles;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Files;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
/// GetFiles / UploadFile / DownloadFile (AB#6182) are thin wrappers over the typed files client of the SDK
/// (AB#6229); these tests pin argument handling, defaults and the local-file safety rules.
/// </summary>
public sealed class PlatformFileCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "octo-cli-files-" + Guid.NewGuid().ToString("N"));
    private readonly IFilesClient _files = A.Fake<IFilesClient>();
    private readonly IAssetServicesClient _client = A.Fake<IAssetServicesClient>();
    private readonly List<string> _lines = [];
    private readonly IConsoleService _console = A.Fake<IConsoleService>();

    public PlatformFileCommandTests()
    {
        Directory.CreateDirectory(_dir);
        A.CallTo(() => _client.Files).Returns(_files);
        A.CallTo(() => _console.WriteLine(A<string>._)).Invokes((string l) => _lines.Add(l));
    }

    public void Dispose() => Directory.Delete(_dir, true);

    private GetFiles NewGet() => new(NullLogger<GetFiles>.Instance, _console, Opts(), _client,
        A.Fake<IAuthenticationService>());

    private UploadFile NewUpload() => new(NullLogger<UploadFile>.Instance, Opts(), _client,
        A.Fake<IAuthenticationService>());

    private DownloadFile NewDownload() => new(NullLogger<DownloadFile>.Instance, Opts(), _client,
        A.Fake<IAuthenticationService>());

    private static IOptions<OctoToolOptions> Opts() => Options.Create(new OctoToolOptions { TenantId = "t1" });

    private static FileEntryDto Entry(string name) => new() { RtId = "r1", Name = name, Kind = "file", Path = name };

    // ── GetFiles ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetFiles_WithoutRoot_ListsRoots()
    {
        A.CallTo(() => _files.ListRootsAsync(A<int>._, A<string?>._, A<CancellationToken>._))
            .Returns(new FilesPage { Items = [Entry("Files")] });
        var cmd = NewGet();
        cmd.CommandArgumentValue.ParseLayer([]);

        await cmd.Execute();

        A.CallTo(() => _files.ListChildrenAsync(A<string>.Ignored!, A<string?>._, A<int>._, A<string?>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        Assert.Contains(_lines, l => l.Contains("Files"));
    }

    [Fact]
    public async Task GetFiles_WithRootAndPath_ListsChildrenAndFollowsPages()
    {
        A.CallTo(() => _files.ListChildrenAsync("Files", "docs", A<int>._, null, A<CancellationToken>._))
            .Returns(new FilesPage { Items = [Entry("a.txt")], HasNextPage = true, EndCursor = "c1" });
        A.CallTo(() => _files.ListChildrenAsync("Files", "docs", A<int>._, "c1", A<CancellationToken>._))
            .Returns(new FilesPage { Items = [Entry("b.txt")] });
        var cmd = NewGet();
        cmd.CommandArgumentValue.ParseLayer(["-r", "Files", "-p", "/docs/"]);

        await cmd.Execute();

        var output = string.Join("\n", _lines);
        Assert.Contains("a.txt", output);
        Assert.Contains("b.txt", output);
    }

    [Fact]
    public async Task GetFiles_PathWithoutRoot_Throws()
    {
        var cmd = NewGet();
        cmd.CommandArgumentValue.ParseLayer(["-p", "docs"]);

        await Assert.ThrowsAsync<ToolException>(() => cmd.Execute());
    }

    // ── UploadFile ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadFile_Defaults_UsesDefaultRootAndFileName()
    {
        var local = Path.Combine(_dir, "report.pdf");
        await File.WriteAllTextAsync(local, "x", TestContext.Current.CancellationToken);
        A.CallTo(() => _files.UploadAsync(A<string>._, A<string>._, A<Stream>._, A<string?>._,
                A<FileConflictMode>._, A<bool>._, A<CancellationToken>._))
            .Returns(Entry("report.pdf"));
        var cmd = NewUpload();
        cmd.CommandArgumentValue.ParseLayer(["-f", local]);

        await cmd.Execute();

        A.CallTo(() => _files.UploadAsync("Files", "report.pdf", A<Stream>._, null, FileConflictMode.Fail, false,
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task UploadFile_WithAllOptions_PassesThemOn()
    {
        var local = Path.Combine(_dir, "a.bin");
        await File.WriteAllTextAsync(local, "x", TestContext.Current.CancellationToken);
        A.CallTo(() => _files.UploadAsync(A<string>._, A<string>._, A<Stream>._, A<string?>._,
                A<FileConflictMode>._, A<bool>._, A<CancellationToken>._))
            .Returns(Entry("a.bin"));
        var cmd = NewUpload();
        cmd.CommandArgumentValue.ParseLayer(["-f", local, "-r", "Docs", "-p", "/x/y/a.bin", "-c", "keepBoth", "-cf",
            "-ct", "application/zip"]);

        await cmd.Execute();

        A.CallTo(() => _files.UploadAsync("Docs", "x/y/a.bin", A<Stream>._, "application/zip",
            FileConflictMode.KeepBoth, true, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task UploadFile_MissingLocalFile_ThrowsWithoutCallingService()
    {
        var cmd = NewUpload();
        cmd.CommandArgumentValue.ParseLayer(["-f", Path.Combine(_dir, "nope.txt")]);

        await Assert.ThrowsAsync<ToolException>(() => cmd.Execute());

        A.CallTo(() => _files.UploadAsync(A<string>._, A<string>._, A<Stream>._, A<string?>._,
            A<FileConflictMode>._, A<bool>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task UploadFile_UnknownConflictMode_Throws()
    {
        var local = Path.Combine(_dir, "a.txt");
        await File.WriteAllTextAsync(local, "x", TestContext.Current.CancellationToken);
        var cmd = NewUpload();
        cmd.CommandArgumentValue.ParseLayer(["-f", local, "-c", "merge"]);

        await Assert.ThrowsAsync<ToolException>(() => cmd.Execute());
    }

    // ── DownloadFile ────────────────────────────────────────────────────────

    private void GivenDownload(string content, string? name = "server.txt") =>
        A.CallTo(() => _files.DownloadAsync(A<string>._, A<string>._, A<bool>._, A<CancellationToken>._))
            .ReturnsLazily(() => new FileDownload(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)), name,
                "text/plain", content.Length));

    [Fact]
    public async Task DownloadFile_ByPath_WritesTargetFile()
    {
        GivenDownload("hello");
        var target = Path.Combine(_dir, "out.txt");
        var cmd = NewDownload();
        cmd.CommandArgumentValue.ParseLayer(["-p", "docs/a.txt", "-f", target]);

        await cmd.Execute();

        Assert.Equal("hello", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        A.CallTo(() => _files.DownloadAsync("Files", "docs/a.txt", false, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        Assert.Empty(Directory.GetFiles(_dir, "*.part"));
    }

    [Fact]
    public async Task DownloadFile_ByRtId_UsesDownloadById()
    {
        A.CallTo(() => _files.DownloadByIdAsync("abc", A<bool>._, A<CancellationToken>._))
            .Returns(new FileDownload(new MemoryStream([1, 2]), "x.bin", null, 2));
        var target = Path.Combine(_dir, "x.bin");
        var cmd = NewDownload();
        cmd.CommandArgumentValue.ParseLayer(["-id", "abc", "-f", target]);

        await cmd.Execute();

        Assert.Equal(2, new FileInfo(target).Length);
    }

    [Fact]
    public async Task DownloadFile_ExistingTarget_RefusesWithoutOverwrite_AndKeepsContent()
    {
        GivenDownload("new");
        var target = Path.Combine(_dir, "out.txt");
        await File.WriteAllTextAsync(target, "old", TestContext.Current.CancellationToken);
        var cmd = NewDownload();
        cmd.CommandArgumentValue.ParseLayer(["-p", "a.txt", "-f", target]);

        await Assert.ThrowsAsync<ToolException>(() => cmd.Execute());

        Assert.Equal("old", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadFile_ExistingTarget_OverwritesWithFlag()
    {
        GivenDownload("new");
        var target = Path.Combine(_dir, "out.txt");
        await File.WriteAllTextAsync(target, "old", TestContext.Current.CancellationToken);
        var cmd = NewDownload();
        cmd.CommandArgumentValue.ParseLayer(["-p", "a.txt", "-f", target, "-o"]);

        await cmd.Execute();

        Assert.Equal("new", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadFile_NeitherOrBothAddresses_Throws()
    {
        var none = NewDownload();
        none.CommandArgumentValue.ParseLayer([]);
        await Assert.ThrowsAsync<ToolException>(() => none.Execute());

        var both = NewDownload();
        both.CommandArgumentValue.ParseLayer(["-p", "a", "-id", "b"]);
        await Assert.ThrowsAsync<ToolException>(() => both.Execute());

        A.CallTo(() => _files.DownloadAsync(A<string>._, A<string>._, A<bool>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }
}
