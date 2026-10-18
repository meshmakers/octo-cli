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
/// AB#6972: ReconcileTenantRegistry is a dry run unless --apply is given, and prints the counts, every insert and
/// every conflict with its reason.
/// </summary>
public sealed class ReconcileTenantRegistryTests
{
    [Fact]
    public async Task Execute_WithoutApply_IsADryRun_AndPrintsCountsInsertsAndConflicts()
    {
        var serviceClient = NewServiceClient(dryRun: true);
        var (console, lines) = NewRecordingConsole();
        var command = NewCommand(serviceClient, console);
        command.CommandArgumentValue.ParseLayer([]);

        await command.Execute();

        A.CallTo(() => serviceClient.ReconcileTenantRegistryAsync(false)).MustHaveHappenedOnceExactly();
        A.CallTo(() => serviceClient.ReconcileTenantRegistryAsync(true)).MustNotHaveHappened();
        Assert.Contains("Dry run: walked 5, to insert 1, already present 3, conflicts 1", lines);
        Assert.Contains(lines, l => l.Contains("ec_subtenant_test") && l.Contains("parent energycommunitymanagement"));
        Assert.Contains(lines, l => l.Contains("conflict: ghost") && l.Contains("registered for tenant"));
        Assert.Contains(lines, l => l.Contains("Nothing was written"));
    }

    [Theory]
    [InlineData("-a")]
    [InlineData("--apply")]
    public async Task Execute_WithApply_WritesAndSaysSo(string flag)
    {
        var serviceClient = NewServiceClient(dryRun: false);
        var (console, lines) = NewRecordingConsole();
        var command = NewCommand(serviceClient, console);
        command.CommandArgumentValue.ParseLayer([flag]);

        await command.Execute();

        A.CallTo(() => serviceClient.ReconcileTenantRegistryAsync(true)).MustHaveHappenedOnceExactly();
        Assert.Contains("Applied: walked 5, inserted 1, already present 3, conflicts 1", lines);
        Assert.DoesNotContain(lines, l => l.Contains("Nothing was written"));
    }

    private static TenantRegistryReconcileReportDto Report(bool applied) => new()
    {
        Applied = applied,
        Walked = 5,
        AlreadyPresent = 3,
        Inserted =
            [new TenantRegistryReconcileInsertDto
            {
                TenantId = "ec_subtenant_test", DatabaseName = "ec_subtenant_test",
                ParentTenantId = "energycommunitymanagement"
            }],
        Conflicts =
            [new TenantRegistryReconcileConflictDto
            {
                TenantId = "ghost", ParentTenantId = "ecm", Reason = "database 'x' is registered for tenant 'y'"
            }]
    };

    private static IAssetServicesClient NewServiceClient(bool dryRun)
    {
        var serviceClient = A.Fake<IAssetServicesClient>();
        A.CallTo(() => serviceClient.ReconcileTenantRegistryAsync(A<bool>._)).Returns(Report(!dryRun));
        return serviceClient;
    }

    private static ReconcileTenantRegistry NewCommand(IAssetServicesClient serviceClient, IConsoleService console) =>
        new(NullLogger<ReconcileTenantRegistry>.Instance, console,
            Options.Create(new OctoToolOptions { TenantId = "octosystem" }), serviceClient,
            A.Fake<IAuthenticationService>());

    private static (IConsoleService Console, List<string> Lines) NewRecordingConsole()
    {
        var lines = new List<string>();
        var console = A.Fake<IConsoleService>();
        A.CallTo(() => console.WriteLine(A<string>._)).Invokes((string line) => lines.Add(line));
        return (console, lines);
    }
}
