using FakeItEasy;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.GraphQl;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>Records what a command writes to the process streams.</summary>
internal sealed class FakeStreams : IStandardStreams
{
    public string? Input { get; set; }
    public List<string> Output { get; } = [];
    public List<string> Error { get; } = [];
    public bool IsInputRedirected => Input != null;

    public Task<string> ReadInputToEndAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Input ?? string.Empty);

    public void WriteOutput(string text) => Output.Add(text);
    public void WriteError(string text) => Error.Add(text);
}

/// <summary>AB#7027: argument handling, query sources, output streams and exit behaviour of <c>GraphQl</c>.</summary>
public sealed class GraphQlCommandTests : IDisposable
{
    private readonly IAssetServicesClient _client = A.Fake<IAssetServicesClient>();
    private readonly string _dir = Directory.CreateTempSubdirectory("graphql-cmd-").FullName;
    private readonly FakeStreams _streams = new();

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public async Task Query_IsSent_ToTheTenantOfTheContext_AndTheResponseIsWrittenUnmodifiedToStandardOutput()
    {
        const string body = """{ "data" :{"a":[1,  2]} }""";
        Answer(body);

        await Run("-q", "{ a }");

        A.CallTo(() => _client.SendGraphQlAsync("ctxtenant", "{ a }", null, null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        Assert.Equal([body], _streams.Output);
        Assert.Empty(_streams.Error);
    }

    [Fact]
    public async Task TenantArgument_WinsOverTheContextTenant_AndIsLowerCased()
    {
        Answer("""{"data":{}}""");

        await Run("-tid", "Child-One", "-q", "{ a }");

        A.CallTo(() => _client.SendGraphQlAsync("child-one", "{ a }", null, null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Query_FromFile()
    {
        Answer("""{"data":{}}""");
        var file = Path.Combine(_dir, "q.graphql");
        await File.WriteAllTextAsync(file, "query { fromFile }", TestContext.Current.CancellationToken);

        await Run("-f", file);

        A.CallTo(() => _client.SendGraphQlAsync("ctxtenant", "query { fromFile }", null, null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Query_FromStandardInput_WithTheStdinFlag()
    {
        Answer("""{"data":{}}""");
        _streams.Input = "query { fromStdin }";

        await Run("-si");

        A.CallTo(() => _client.SendGraphQlAsync("ctxtenant", "query { fromStdin }", null, null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Query_FromStandardInput_WhenInputIsPiped_AndNoQueryIsGiven()
    {
        Answer("""{"data":{}}""");
        _streams.Input = "{ piped }";

        await Run();

        A.CallTo(() => _client.SendGraphQlAsync("ctxtenant", "{ piped }", null, null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task NoQuery_AndNoPipedInput_IsRefused_WithAHint()
    {
        var ex = await Assert.ThrowsAsync<ToolException>(() => Run());

        Assert.Contains("--query", ex.Message);
        A.CallTo(_client).MustNotHaveHappened();
    }

    [Fact]
    public async Task QueryAndFile_TogetherAreRefused()
    {
        await Assert.ThrowsAsync<ToolException>(() => Run("-q", "{ a }", "-f", "x.graphql"));
        _streams.Input = "{ a }";
        await Assert.ThrowsAsync<ToolException>(() => Run("-q", "{ a }", "-si"));

        A.CallTo(_client).MustNotHaveHappened();
    }

    [Fact]
    public async Task EmptyDocument_IsRefused()
    {
        _streams.Input = "  \n";

        await Assert.ThrowsAsync<ToolException>(() => Run());

        A.CallTo(_client).MustNotHaveHappened();
    }

    [Fact]
    public async Task MissingFile_IsReported()
    {
        var ex = await Assert.ThrowsAsync<ToolException>(() => Run("-f", Path.Combine(_dir, "nope.graphql")));

        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public async Task Variables_AsJson_AndOperationName_AreForwarded()
    {
        Answer("""{"data":{}}""");

        await Run("-q", "query Q($n:Int){a}", "-vars", """{"n":3}""", "-on", "Q");

        A.CallTo(() => _client.SendGraphQlAsync("ctxtenant", "query Q($n:Int){a}", """{"n":3}""", "Q",
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Variables_FromFile()
    {
        Answer("""{"data":{}}""");
        var file = Path.Combine(_dir, "vars.json");
        await File.WriteAllTextAsync(file, """{"n":4}""", TestContext.Current.CancellationToken);

        await Run("-q", "{ a }", "-vars", "@" + file);

        A.CallTo(() => _client.SendGraphQlAsync("ctxtenant", "{ a }", """{"n":4}""", null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task InvalidVariables_BecomeAToolException_NotACrash()
    {
        A.CallTo(() => _client.SendGraphQlAsync(A<string>._, A<string>._, A<string?>._, A<string?>._,
                A<CancellationToken>._))
            .Throws(new ArgumentException("The variables must be a JSON object.", "variablesJson"));

        var ex = await Assert.ThrowsAsync<ToolException>(() => Run("-q", "{ a }", "-vars", "[1]"));

        Assert.Contains("JSON object", ex.Message);
        Assert.Empty(_streams.Output);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(400)]
    public async Task GraphQlErrors_GiveAToolException_TheBodyOnStandardError_AndNothingOnStandardOutput(int status)
    {
        const string body = """{"errors":[{"message":"boom"}]}""";
        Answer(body, (System.Net.HttpStatusCode)status);

        var ex = await Assert.ThrowsAsync<ToolException>(() => Run("-q", "{ a }"));

        Assert.Equal([body], _streams.Error);
        Assert.Empty(_streams.Output);
        Assert.Contains("ctxtenant", ex.Message);
        Assert.DoesNotContain("boom", ex.Message);
    }

    [Fact]
    public async Task NoTenant_AtAll_IsRefused()
    {
        var command = Create(tenant: null);
        command.CommandArgumentValue.ParseLayer(["-q", "{ a }"]);

        await Assert.ThrowsAsync<ToolException>(command.Execute);
    }

    [Fact]
    public async Task SuccessWithoutErrorsArray_AndEmptyErrorsArray_AreSuccess()
    {
        Answer("""{"data":{},"errors":[]}""");

        await Run("-q", "{ a }");

        Assert.Single(_streams.Output);
    }

    private void Answer(string body, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK) =>
        A.CallTo(() => _client.SendGraphQlAsync(A<string>._, A<string>._, A<string?>._, A<string?>._,
                A<CancellationToken>._))
            .Returns(new GraphQlRawResponse(body, status));

    private GraphQlCommand Create(string? tenant = "ctxtenant") =>
        new(new RecordingLogger<GraphQlCommand>(), Options.Create(new OctoToolOptions { TenantId = tenant }), _client,
            A.Fake<IAuthenticationService>(), _streams);

    private async Task Run(params string[] args)
    {
        var command = Create();
        command.CommandArgumentValue.ParseLayer(args);
        await command.Execute();
    }
}
