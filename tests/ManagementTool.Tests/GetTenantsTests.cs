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
/// GetTenants drives every workload roll-out CD's tenant enumeration. The plain call returns
/// DIRECT children only (AB#5025) — silently omitting sub-tenants and re-parented tenants, which
/// is exactly how the roll-outs lost them (AB#5151, AB#5129). --all is the installation-wide
/// enumeration; which of the two service calls the flag selects is pinned here.
/// </summary>
public sealed class GetTenantsTests
{
    [Fact]
    public async Task Execute_WithoutAll_ReturnsDirectChildrenOnly()
    {
        var serviceClient = NewServiceClient(
            children: [Tenant("child-a"), Tenant("child-b")],
            registry: [Tenant("child-a"), Tenant("child-b"), Tenant("nested-under-child-a")]);
        var (console, lines) = NewRecordingConsole();
        var command = NewCommand(serviceClient, console);
        command.CommandArgumentValue.ParseLayer([]);

        await command.Execute();

        A.CallTo(() => serviceClient.GetTenantsAsync()).MustHaveHappenedOnceExactly();
        A.CallTo(() => serviceClient.GetAllTenantsAsync()).MustNotHaveHappened();
        var output = string.Join("\n", lines);
        Assert.Contains("child-a", output);
        Assert.DoesNotContain("nested-under-child-a", output);
    }

    [Fact]
    public async Task Execute_WithAll_ReturnsTheFullRegistry_IncludingNestedTenants()
    {
        var serviceClient = NewServiceClient(
            children: [Tenant("child-a")],
            registry: [Tenant("child-a"), Tenant("nested-under-child-a"), Tenant("reparented")]);
        var (console, lines) = NewRecordingConsole();
        var command = NewCommand(serviceClient, console);
        command.CommandArgumentValue.ParseLayer(["-a"]);

        await command.Execute();

        A.CallTo(() => serviceClient.GetAllTenantsAsync()).MustHaveHappenedOnceExactly();
        A.CallTo(() => serviceClient.GetTenantsAsync()).MustNotHaveHappened();
        var output = string.Join("\n", lines);
        Assert.Contains("nested-under-child-a", output);
        Assert.Contains("reparented", output);
    }

    [Fact]
    public async Task Execute_WithAllLongForm_UsesTheRegistryToo()
    {
        var serviceClient = NewServiceClient(children: [], registry: [Tenant("nested")]);
        var (console, lines) = NewRecordingConsole();
        var command = NewCommand(serviceClient, console);
        command.CommandArgumentValue.ParseLayer(["--all"]);

        await command.Execute();

        A.CallTo(() => serviceClient.GetAllTenantsAsync()).MustHaveHappenedOnceExactly();
        Assert.Contains(lines, l => l.Contains("nested"));
    }

    [Fact]
    public async Task Execute_PrintsNothing_WhenNoTenantIsReturned()
    {
        var serviceClient = NewServiceClient(children: [], registry: []);
        var (console, lines) = NewRecordingConsole();
        var command = NewCommand(serviceClient, console);
        command.CommandArgumentValue.ParseLayer([]);

        await command.Execute();

        Assert.Empty(lines);
    }

    private static TenantDto Tenant(string tenantId) =>
        new() { TenantId = tenantId, Database = $"{tenantId}-db" };

    private static IAssetServicesClient NewServiceClient(TenantDto[] children, TenantDto[] registry)
    {
        var serviceClient = A.Fake<IAssetServicesClient>();
        A.CallTo(() => serviceClient.GetTenantsAsync()).Returns(children);
        A.CallTo(() => serviceClient.GetAllTenantsAsync()).Returns(registry);
        return serviceClient;
    }

    private static GetTenants NewCommand(IAssetServicesClient serviceClient, IConsoleService console) =>
        new(NullLogger<GetTenants>.Instance, console,
            Options.Create(new OctoToolOptions { TenantId = "octosystem" }), serviceClient,
            A.Fake<IAuthenticationService>());

    private static (IConsoleService Console, List<string> Lines) NewRecordingConsole()
    {
        var lines = new List<string>();
        var console = A.Fake<IConsoleService>();
        A.CallTo(() => console.WriteLine(A<string>._))
            .Invokes((string line) => lines.Add(line));
        return (console, lines);
    }
}
