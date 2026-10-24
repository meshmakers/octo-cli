using System.Net;
using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace ManagementTool.Tests;

/// <summary>AB#7429: <c>GetAdapter</c> shows the typed CPU/memory settings and the last deployment warning.</summary>
public sealed class GetAdapterResourcesTests
{
    private const string AdapterId = "670000000000000000000001";

    private static string Run(IAssetServicesClient assetClient, params string[] args)
    {
        var client = A.Fake<ICommunicationServicesClient>();
        A.CallTo(() => client.GetAdapterConfigurationAsync(AdapterId))
            .Returns(Task.FromResult(new AdapterConfigurationDto(
                new RtEntityId($"System.Communication/MeshAdapter@{AdapterId}"), """{"Host":"h"}""", [])));
        var console = new SwitchServiceAccountsToImpersonationCommandTests.RecordingConsoleService();
        var command = new GetAdapterCommand(new RecordingLogger<GetAdapterCommand>(),
            Options.Create(new OctoToolOptions { TenantId = "acme" }), console, client,
            A.Fake<IAuthenticationService>(), assetClient);
        command.CommandArgumentValue.ParseLayer(["-id", AdapterId, .. args]);
        command.Execute().GetAwaiter().GetResult();
        return string.Join('\n', console.Lines);
    }

    private static IAssetServicesClient AssetClientAnswering(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var assetClient = A.Fake<IAssetServicesClient>();
        A.CallTo(() => assetClient.SendGraphQlAsync("acme", A<string>._, A<string?>._, A<string?>._,
                A<CancellationToken>._))
            .Returns(new GraphQlRawResponse(body, status));
        return assetClient;
    }

    private const string AdapterWithSizing =
        """{"data":{"runtime":{"adapters":{"items":[{"rtId":"670000000000000000000001","name":"a","cpuRequest":null,"cpuLimit":null,"memoryRequest":"512Mi","memoryLimit":"1Gi","lastDeploymentWarning":"MemoryRequest 512Mi is overridden by an explicit Helm value (ValuesYaml resources.requests.memory)"}]},"applications":{"items":[]}}}}""";

    [Fact]
    public void Output_CarriesTheFourValues_AndTheWarning_AndNamesTheChartDefault()
    {
        var output = JObject.Parse(Run(AssetClientAnswering(AdapterWithSizing)));

        var resources = (JObject)output["resources"]!;
        Assert.Equal("512Mi", (string?)resources["memoryRequest"]);
        Assert.Equal("1Gi", (string?)resources["memoryLimit"]);
        Assert.Equal("chart default", (string?)resources["cpuRequest"]);
        Assert.Equal("chart default", (string?)resources["cpuLimit"]);
        Assert.Contains("overridden by an explicit Helm value", (string?)resources["lastDeploymentWarning"]);
    }

    [Fact]
    public void JsonOutput_UsesNullForUnsetValues()
    {
        var output = JObject.Parse(Run(AssetClientAnswering(AdapterWithSizing), "-j"));

        Assert.Equal(JTokenType.Null, output["resources"]!["cpuRequest"]!.Type);
    }

    [Fact]
    public void TenantWithoutTheAttributes_StillShowsTheAdapter_WithoutResources()
    {
        var older = AssetClientAnswering(
            """{"errors":[{"message":"Cannot query field \"cpuRequest\" on type \"SystemCommunicationAdapter\"."}]}""",
            HttpStatusCode.BadRequest);

        var output = JObject.Parse(Run(older));

        Assert.NotEmpty(output.Properties());
        Assert.Null(output["resources"]);
    }
}
