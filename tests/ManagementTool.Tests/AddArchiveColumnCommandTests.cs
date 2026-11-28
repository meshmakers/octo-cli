using FakeItEasy;
using GraphQL;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#7997 — AddArchiveColumn adds the path as an ingested column, not indexed unless asked; since AB#9060 through the
///     method <c>Archive.AddColumn-1</c> of the archive's concrete type, never through the REST client.
/// </summary>
public sealed class AddArchiveColumnCommandTests
{
    private const string Archive = "6a0e000000000000000a000d";

    private readonly ITenantClient _tenant = A.Fake<ITenantClient>();
    private readonly List<GraphQLRequest> _requests = [];

    public AddArchiveColumnCommandTests()
    {
        A.CallTo(() => _tenant.SendQueryAsync<ArchiveMethodCall.ArchiveTypeDto>(A<GraphQLRequest>._)).Returns(
            new QlItemsContainer<ArchiveMethodCall.ArchiveTypeDto>
            {
                Items = [new ArchiveMethodCall.ArchiveTypeDto { RtId = Archive, CkTypeId = "System.StreamData/RawArchive" }]
            });
        MethodFakes.AnswerMethodCalls(_tenant, _requests, null);
    }

    private AddArchiveColumnCommand Command(IStreamDataServicesClient client, string? tenant = "acme") =>
        new(new RecordingLogger<AddArchiveColumnCommand>(), Options.Create(new OctoToolOptions { TenantId = tenant }),
            client, _tenant, A.Fake<IAuthenticationService>());

    [Fact]
    public async Task AddsTheColumn_NotIndexedByDefault()
    {
        var client = A.Fake<IStreamDataServicesClient>();
        var command = Command(client);
        command.CommandArgumentValue.ParseLayer(["-id", Archive, "-p", "DischargingPower"]);

        await command.Execute();

        var request = Assert.Single(_requests);
        Assert.Contains("systemStreamDataRawArchives { addColumn(", request.Query, StringComparison.Ordinal);
        Assert.Contains("\"path\":\"DischargingPower\"", Variables(request), StringComparison.Ordinal);
        Assert.Contains("\"indexed\":false", Variables(request), StringComparison.Ordinal);
        A.CallTo(() => client.AddArchiveColumnAsync(A<string>._, A<string>._, A<string>._, A<bool>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task AddsTheColumn_Indexed_WhenAsked()
    {
        var client = A.Fake<IStreamDataServicesClient>();
        var command = Command(client);
        command.CommandArgumentValue.ParseLayer(["-id", Archive, "-p", "Amount.Value", "-x", "true"]);

        await command.Execute();

        var request = Assert.Single(_requests);
        Assert.Contains("\"path\":\"Amount.Value\"", Variables(request), StringComparison.Ordinal);
        Assert.Contains("\"indexed\":true", Variables(request), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutATenant_CallsNothing()
    {
        var client = A.Fake<IStreamDataServicesClient>();
        var command = Command(client, tenant: null);
        command.CommandArgumentValue.ParseLayer(["-id", Archive, "-p", "DischargingPower"]);

        await command.Execute();

        A.CallTo(client).MustNotHaveHappened();
        Assert.Empty(_requests);
        A.CallTo(_tenant).Where(c => c.Method.Name == nameof(ITenantClient.SendQueryAsync)).MustNotHaveHappened();
    }

    private static string Variables(GraphQLRequest request) => System.Text.Json.JsonSerializer.Serialize(request.Variables);
}
