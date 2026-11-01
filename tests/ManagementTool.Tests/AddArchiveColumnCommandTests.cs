using FakeItEasy;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>AB#7997 — AddArchiveColumn hands the path to the stream data client, not indexed unless asked.</summary>
public sealed class AddArchiveColumnCommandTests
{
    private const string Archive = "6a0e000000000000000a000d";

    private static AddArchiveColumnCommand Command(IStreamDataServicesClient client, string? tenant = "acme") =>
        new(new RecordingLogger<AddArchiveColumnCommand>(), Options.Create(new OctoToolOptions { TenantId = tenant }),
            client, A.Fake<IAuthenticationService>());

    [Fact]
    public async Task AddsTheColumn_NotIndexedByDefault()
    {
        var client = A.Fake<IStreamDataServicesClient>();
        var command = Command(client);
        command.CommandArgumentValue.ParseLayer(["-id", Archive, "-p", "DischargingPower"]);

        await command.Execute();

        A.CallTo(() => client.AddArchiveColumnAsync("acme", Archive, "DischargingPower", false)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task AddsTheColumn_Indexed_WhenAsked()
    {
        var client = A.Fake<IStreamDataServicesClient>();
        var command = Command(client);
        command.CommandArgumentValue.ParseLayer(["-id", Archive, "-p", "Amount.Value", "-x", "true"]);

        await command.Execute();

        A.CallTo(() => client.AddArchiveColumnAsync("acme", Archive, "Amount.Value", true)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task WithoutATenant_CallsNothing()
    {
        var client = A.Fake<IStreamDataServicesClient>();
        var command = Command(client, tenant: null);
        command.CommandArgumentValue.ParseLayer(["-id", Archive, "-p", "DischargingPower"]);

        await command.Execute();

        A.CallTo(client).MustNotHaveHappened();
    }
}
