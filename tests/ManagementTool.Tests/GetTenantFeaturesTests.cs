using FakeItEasy;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
/// GetTenantFeatures reports the four per-tenant capability flags the tenant delete/detach guard
/// evaluates (AB#4255), in guard order, one line per capability (AB#4884). The line wording is
/// pinned here because operators read it to decide which Disable* commands a Delete/Detach still
/// needs.
/// </summary>
public sealed class GetTenantFeaturesTests
{
    [Fact]
    public async Task Execute_PrintsOneLinePerCapability_InGuardOrder()
    {
        var serviceClient = NewServiceClient(new TenantFeaturesStatusDto
        {
            StreamData = new StreamDataFeatureStatusDto { InstanceEnabled = true, TenantEnabled = true },
            Communication = new TenantFeatureStatusDto { TenantEnabled = true },
            Reporting = new TenantFeatureStatusDto { TenantEnabled = false },
            AiServices = new TenantFeatureStatusDto { TenantEnabled = true },
        });
        var (console, rows) = NewRecordingConsole();

        await NewCommand(serviceClient, console, tenantId: "meshtest").Execute();

        A.CallTo(() => serviceClient.GetTenantFeaturesStatusAsync()).MustHaveHappenedOnceExactly();
        Assert.Equal(
        [
            ("CAPABILITY", "STATE"),
            ("Stream Data", "Enabled"),
            ("Communication", "Enabled"),
            ("Reporting", "Disabled"),
            ("AI Services", "Enabled"),
        ], rows);
    }

    [Fact]
    public async Task Execute_NotesInstanceSwitch_WhenStreamDataIsOffAtInstanceLevel()
    {
        // The tenant flag is reported as-is even on an installation without stream data —
        // the note makes the discrepancy visible instead of hiding the flag.
        var serviceClient = NewServiceClient(new TenantFeaturesStatusDto
        {
            StreamData = new StreamDataFeatureStatusDto { InstanceEnabled = false, TenantEnabled = true },
            Communication = new TenantFeatureStatusDto { TenantEnabled = false },
            Reporting = new TenantFeatureStatusDto { TenantEnabled = false },
            AiServices = new TenantFeatureStatusDto { TenantEnabled = false },
        });
        var (console, rows) = NewRecordingConsole();

        await NewCommand(serviceClient, console, tenantId: "meshtest").Execute();

        Assert.Contains(("Stream Data", "Enabled (stream data is switched off at the instance level)"), rows);
    }

    [Fact]
    public async Task Execute_TreatsMissingCapabilitySections_AsDisabled()
    {
        var serviceClient = NewServiceClient(new TenantFeaturesStatusDto());
        var (console, rows) = NewRecordingConsole();

        await NewCommand(serviceClient, console, tenantId: "meshtest").Execute();

        Assert.Equal(
        [
            ("CAPABILITY", "STATE"),
            ("Stream Data", "Disabled"),
            ("Communication", "Disabled"),
            ("Reporting", "Disabled"),
            ("AI Services", "Disabled"),
        ], rows);
    }

    [Fact]
    public async Task Execute_WithoutTenantInContext_DoesNotCallTheService()
    {
        var serviceClient = NewServiceClient(new TenantFeaturesStatusDto());
        var (console, rows) = NewRecordingConsole();

        await NewCommand(serviceClient, console, tenantId: null).Execute();

        A.CallTo(() => serviceClient.GetTenantFeaturesStatusAsync()).MustNotHaveHappened();
        Assert.Empty(rows);
    }

    private static GetTenantFeatures NewCommand(IAssetServicesClient serviceClient, IConsoleService console,
        string? tenantId) =>
        new(NullLogger<GetTenantFeatures>.Instance, console,
            Options.Create(new OctoToolOptions { TenantId = tenantId }), serviceClient,
            A.Fake<IAuthenticationService>());

    private static IAssetServicesClient NewServiceClient(TenantFeaturesStatusDto status)
    {
        var serviceClient = A.Fake<IAssetServicesClient>();
        A.CallTo(() => serviceClient.GetTenantFeaturesStatusAsync()).Returns(status);
        return serviceClient;
    }

    private static (IConsoleService Console, List<(string, string)> Rows) NewRecordingConsole()
    {
        var rows = new List<(string, string)>();
        var console = A.Fake<IConsoleService>();
        A.CallTo(() => console.WriteColumnLine(A<string>._, A<int>._, A<string>._))
            .Invokes((string column1, int _, string column2) => rows.Add((column1, column2)));
        return (console, rows);
    }
}
