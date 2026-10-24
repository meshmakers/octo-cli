using System.Net;
using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services.WorkloadResources;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#7429: <c>UpdateWorkloadResources</c> sets, clears and shows the typed CPU and memory settings through the
///     tenant's GraphQL endpoint. The tests answer the GraphQL calls with canned JSON, so they run without the
///     published System.Communication 4.10.0 model.
/// </summary>
public sealed class UpdateWorkloadResourcesCommandTests
{
    private const string RtId = "670000000000000000000001";

    private readonly IAssetServicesClient _client = A.Fake<IAssetServicesClient>();
    private readonly SwitchServiceAccountsToImpersonationCommandTests.RecordingConsoleService _console = new();
    private readonly List<(string Query, string? Variables)> _mutations = [];

    private UpdateWorkloadResourcesCommand Create(string? tenant = "Acme")
    {
        var command = new UpdateWorkloadResourcesCommand(new RecordingLogger<UpdateWorkloadResourcesCommand>(),
            Options.Create(new OctoToolOptions { TenantId = tenant }), _console, _client,
            A.Fake<IAuthenticationService>());
        return command;
    }

    private async Task Run(params string[] args)
    {
        var command = Create();
        command.CommandArgumentValue.ParseLayer(["-id", RtId, .. args]);
        await command.Execute();
    }

    private void GivenAdapter(string? cpuRequest = null, string? cpuLimit = null, string? memoryRequest = null,
        string? memoryLimit = null, string? warning = null) =>
        Answer(Item("adapters", cpuRequest, cpuLimit, memoryRequest, memoryLimit, warning), "[]");

    private void GivenApplication() =>
        Answer("[]", Item("applications", null, null, null, null, null));

    private static string Item(string _, string? cr, string? cl, string? mr, string? ml, string? warning) =>
        new JsonArray(new JsonObject
        {
            ["rtId"] = RtId, ["name"] = "demo", ["cpuRequest"] = cr, ["cpuLimit"] = cl, ["memoryRequest"] = mr,
            ["memoryLimit"] = ml, ["lastDeploymentWarning"] = warning
        }).ToJsonString();

    private void Answer(string adapterItems, string applicationItems)
    {
        A.CallTo(() => _client.SendGraphQlAsync(A<string>._, A<string>.That.StartsWith("query"), A<string?>._,
                A<string?>._, A<CancellationToken>._))
            .Returns(new GraphQlRawResponse(
                "{\"data\":{\"runtime\":{\"adapters\":{\"items\":" + adapterItems +
                "},\"applications\":{\"items\":" + applicationItems + "}}}}",
                HttpStatusCode.OK));
        A.CallTo(() => _client.SendGraphQlAsync(A<string>._, A<string>.That.StartsWith("mutation"), A<string?>._,
                A<string?>._, A<CancellationToken>._))
            .ReturnsLazily((string _, string query, string? variables, string? _, CancellationToken _) =>
            {
                _mutations.Add((query, variables));
                var item = JsonNode.Parse(variables!)!["entity"]!["item"]!.AsObject();
                var field = query.Contains("systemCommunicationAdapters") ? "systemCommunicationAdapters" : "systemCommunicationApplications";
                var stored = new JsonObject
                {
                    ["rtId"] = RtId, ["name"] = "demo", ["cpuRequest"] = null, ["cpuLimit"] = null,
                    ["memoryRequest"] = null, ["memoryLimit"] = null, ["lastDeploymentWarning"] = null
                };
                foreach (var (key, value) in item)
                {
                    stored[key] = value?.DeepClone();
                }

                return new GraphQlRawResponse(
                    new JsonObject
                    {
                        ["data"] = new JsonObject
                            { ["runtime"] = new JsonObject { [field] = new JsonObject { ["update"] = new JsonArray(stored) } } }
                    }.ToJsonString(), HttpStatusCode.OK);
            });
    }

    private JsonObject SentItem()
    {
        var mutation = Assert.Single(_mutations);
        return JsonNode.Parse(mutation.Variables!)!["entity"]!["item"]!.AsObject();
    }

    [Fact]
    public async Task SettingOneValue_WritesOnlyThatValue_AndRemindsToDeploy()
    {
        GivenAdapter(memoryLimit: "1Gi");

        await Run("--memory-request", "512Mi");

        var item = SentItem();
        Assert.Equal(["memoryRequest"], item.Select(p => p.Key));
        Assert.Equal("512Mi", item["memoryRequest"]!.GetValue<string>());
        var mutation = Assert.Single(_mutations);
        Assert.Equal(WorkloadResourcesAccess.AdapterMutation, mutation.Query);
        Assert.Equal(RtId, JsonNode.Parse(mutation.Variables!)!["entity"]!["rtId"]!.GetValue<string>());
        Assert.Contains(_console.Lines, l => l.Contains("MemoryRequest:") && l.Contains("512Mi"));
        Assert.Contains(_console.Lines, l => l.Contains("DeployWorkload") && l.Contains(RtId));
    }

    [Fact]
    public async Task TenantOfTheContext_IsLowerCased()
    {
        GivenAdapter();

        await Run("-mr", "512Mi");

        A.CallTo(() => _client.SendGraphQlAsync("acme", A<string>._, A<string?>._, A<string?>._,
            A<CancellationToken>._)).MustHaveHappened();
    }

    [Fact]
    public async Task EmptyValue_ClearsTheAttribute()
    {
        GivenAdapter(memoryLimit: "1Gi");

        await Run("--memory-limit", "");

        AssertLimitCleared();
    }

    [Fact]
    public async Task OptionWithoutValue_ClearsTheAttribute()
    {
        GivenAdapter(memoryLimit: "1Gi");

        await Run("--memory-limit");

        AssertLimitCleared();
    }

    private void AssertLimitCleared()
    {
        var item = SentItem();
        Assert.True(item.ContainsKey("memoryLimit"));
        Assert.Null(item["memoryLimit"]);
        Assert.Contains(_console.Lines, l => l.Contains("MemoryLimit:") && l.Contains("(chart default)"));
    }

    [Fact]
    public async Task Application_IsWrittenThroughTheApplicationMutation()
    {
        GivenApplication();

        await Run("--cpu-request", "250m");

        var mutation = Assert.Single(_mutations);
        Assert.Equal(WorkloadResourcesAccess.ApplicationMutation, mutation.Query);
        Assert.Contains(_console.Lines, l => l.StartsWith("Application 'demo'"));
    }

    [Fact]
    public async Task InvalidQuantity_IsRejected_BeforeAnythingIsWritten()
    {
        GivenAdapter();

        var ex = await Assert.ThrowsAsync<ToolException>(() => Run("--memory-request", "512 MB"));

        Assert.Contains("MemoryRequest '512 MB' is not a valid Kubernetes quantity", ex.Message);
        Assert.Empty(_mutations);
    }

    [Fact]
    public async Task RequestAboveLimit_IsRejected_BeforeAnythingIsWritten()
    {
        GivenAdapter();

        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            Run("--memory-request", "2Gi", "--memory-limit", "1Gi"));

        Assert.Contains("MemoryRequest 2Gi exceeds MemoryLimit 1Gi", ex.Message);
        Assert.Empty(_mutations);
    }

    [Fact]
    public async Task RequestAboveTheStoredLimit_IsRejected_WithoutTouchingTheLimit()
    {
        GivenAdapter(cpuLimit: "250m");

        var ex = await Assert.ThrowsAsync<ToolException>(() => Run("--cpu-request", "500m"));

        Assert.Contains("CpuRequest 500m exceeds CpuLimit 250m", ex.Message);
        Assert.Empty(_mutations);
    }

    [Fact]
    public async Task ClearingTheLimit_IsAllowedEvenWhenARequestRemains()
    {
        GivenAdapter(memoryRequest: "512Mi", memoryLimit: "1Gi");

        await Run("--memory-limit", "");

        Assert.Single(_mutations);
    }

    [Fact]
    public async Task WithoutValueOptions_OnlyShowsTheCurrentValues()
    {
        GivenAdapter(memoryRequest: "512Mi", warning: "MemoryRequest 512Mi is overridden by an explicit Helm value");

        await Run();

        Assert.Empty(_mutations);
        Assert.Contains(_console.Lines, l => l.Contains("MemoryRequest:") && l.Contains("512Mi"));
        Assert.Contains(_console.Lines, l => l.Contains("CpuRequest:") && l.Contains("(chart default)"));
        Assert.Contains(_console.Lines, l => l.Contains("LastDeploymentWarning:") && l.Contains("overridden"));
    }

    [Fact]
    public async Task UnknownWorkload_IsReported()
    {
        Answer("[]", "[]");

        var ex = await Assert.ThrowsAsync<ToolException>(() => Run("-mr", "512Mi"));

        Assert.Contains(RtId, ex.Message);
        Assert.Empty(_mutations);
    }

    [Fact]
    public async Task TenantWithoutTheAttributes_GetsAnUpdateHint()
    {
        A.CallTo(() => _client.SendGraphQlAsync(A<string>._, A<string>._, A<string?>._, A<string?>._,
                A<CancellationToken>._))
            .Returns(new GraphQlRawResponse(
                """{"errors":[{"message":"Cannot query field \"cpuRequest\" on type \"SystemCommunicationAdapter\"."}]}""",
                HttpStatusCode.BadRequest));

        var ex = await Assert.ThrowsAsync<ToolException>(() => Run());

        Assert.Contains("4.10.0", ex.Message);
    }

    [Fact]
    public async Task MissingTenant_IsReported()
    {
        var command = Create(tenant: null);
        command.CommandArgumentValue.ParseLayer(["-id", RtId]);

        await Assert.ThrowsAsync<ToolException>(() => command.Execute());
    }
}
