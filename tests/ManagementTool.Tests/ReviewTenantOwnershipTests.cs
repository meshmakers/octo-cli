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
/// AB#8796: ReviewTenantOwnership only lists unless --stamp is given, and prints the counts and every listed
/// database with its marker owner and registering system databases.
/// </summary>
public sealed class ReviewTenantOwnershipTests
{
    [Fact]
    public async Task Execute_WithoutStamp_ListsOnly_AndPrintsEveryListedDatabase()
    {
        var serviceClient = NewServiceClient(stamped: false);
        var (console, lines) = NewRecordingConsole();
        var command = NewCommand(serviceClient, console);
        command.CommandArgumentValue.ParseLayer([]);

        await command.Execute();

        A.CallTo(() => serviceClient.ReviewTenantOwnershipAsync(false)).MustHaveHappenedOnceExactly();
        A.CallTo(() => serviceClient.ReviewTenantOwnershipAsync(true)).MustNotHaveHappened();
        Assert.Contains("List on instance 'octosystem': checked 4, owned 1, listed 2, stamped 0, missing databases 0",
            lines);
        Assert.Contains(lines, l => l.Contains("Unmarked: legacy") && l.Contains("marker none"));
        Assert.Contains(lines, l => l.Contains("RegisteredByOtherInstance: shared") &&
                                    l.Contains("registered in octosystem, octosystemdev"));
        Assert.Contains(lines, l => l.Contains("Nothing was written"));
    }

    [Theory]
    [InlineData("-s")]
    [InlineData("--stamp")]
    public async Task Execute_WithStamp_StampsAndSaysSo(string flag)
    {
        var serviceClient = NewServiceClient(stamped: true);
        var (console, lines) = NewRecordingConsole();
        var command = NewCommand(serviceClient, console);
        command.CommandArgumentValue.ParseLayer([flag]);

        await command.Execute();

        A.CallTo(() => serviceClient.ReviewTenantOwnershipAsync(true)).MustHaveHappenedOnceExactly();
        Assert.Contains(lines, l => l.StartsWith("Stamp on instance 'octosystem'") && l.Contains("stamped 1"));
        Assert.Contains(lines, l => l.Contains("stamped: legacy"));
        Assert.DoesNotContain(lines, l => l.Contains("Nothing was written"));
    }

    private static TenantOwnershipReviewReportDto Report(bool stamped) => new()
    {
        SystemDatabaseName = "octosystem",
        Stamp = stamped,
        Checked = 4,
        Owned = 1,
        Entries =
        [
            new TenantOwnershipReviewEntryDto
            {
                TenantId = "legacy", DatabaseName = "legacy", Issue = "Unmarked",
                MarkerOwnerSystemDatabaseName = stamped ? "octosystem" : null,
                RegisteringSystemDatabases = ["octosystem"], Stamped = stamped
            },
            new TenantOwnershipReviewEntryDto
            {
                TenantId = "shared", DatabaseName = "shared", Issue = "RegisteredByOtherInstance",
                RegisteringSystemDatabases = ["octosystem", "octosystemdev"]
            }
        ]
    };

    private static IAssetServicesClient NewServiceClient(bool stamped)
    {
        var serviceClient = A.Fake<IAssetServicesClient>();
        A.CallTo(() => serviceClient.ReviewTenantOwnershipAsync(A<bool>._)).Returns(Report(stamped));
        return serviceClient;
    }

    private static ReviewTenantOwnership NewCommand(IAssetServicesClient serviceClient, IConsoleService console) =>
        new(NullLogger<ReviewTenantOwnership>.Instance, console,
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
