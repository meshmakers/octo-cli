using FakeItEasy;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#7362 — ListRollupsForArchive shows the reference time zone and the day start of a rollup.
/// </summary>
public sealed class ListRollupsForArchiveCommandTests
{
    private const string ArchiveId = "6700000000000000000000b1";

    private static RollupArchiveInfoDto Rollup(string rtId, string name, string? zone, long? dayStartOffsetMs) =>
        new(rtId, name, "Activated", ArchiveId, 86_400_000, 60_000, null, null, 1, false, null, null, null, null, 0, 0)
        {
            ReferenceTimeZone = zone,
            DayStartOffsetMs = dayStartOffsetMs
        };

    [Fact]
    public async Task GasDayRollup_ShowsTimeZoneAndDayStart_NextToTheExistingFields()
    {
        var (command, client, logger) = NewCommand();
        A.CallTo(() => client.ListRollupsForArchiveAsync("acme", ArchiveId)).Returns(
        [
            Rollup("6700000000000000000000a1", "gasday", "Europe/Vienna", 21_600_000),
            Rollup("6700000000000000000000a2", "daily", "Europe/Vienna", 0),
            Rollup("6700000000000000000000a3", "hourly", null, null)
        ]);
        command.CommandArgumentValue.ParseLayer(["-id", ArchiveId]);

        await command.Execute();

        var gasDay = Assert.Single(logger.Entries, e => e.Message.Contains("(gasday)"));
        Assert.Contains("bucket=86400000ms", gasDay.Message);
        Assert.Contains("aggs=1", gasDay.Message);
        Assert.EndsWith("timeZone=Europe/Vienna, dayStart=06:00", gasDay.Message);
        Assert.EndsWith("timeZone=Europe/Vienna, dayStart=00:00",
            Assert.Single(logger.Entries, e => e.Message.Contains("(daily)")).Message);
        Assert.EndsWith("timeZone=<none>, dayStart=<none>",
            Assert.Single(logger.Entries, e => e.Message.Contains("(hourly)")).Message);
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Theory]
    [InlineData(21_600_000L, "06:00")]
    [InlineData(0L, "00:00")]
    [InlineData(84_600_000L, "23:30")]
    [InlineData(null, "<none>")]
    public void FormatDayStart_IsLocalHoursAndMinutes(long? dayStartOffsetMs, string expected) =>
        Assert.Equal(expected, ListRollupsForArchiveCommand.FormatDayStart(dayStartOffsetMs));

    private static (ListRollupsForArchiveCommand Command, IStreamDataServicesClient Client,
        RecordingLogger<ListRollupsForArchiveCommand> Logger) NewCommand()
    {
        var client = A.Fake<IStreamDataServicesClient>();
        var logger = new RecordingLogger<ListRollupsForArchiveCommand>();
        var command = new ListRollupsForArchiveCommand(logger,
            Options.Create(new OctoToolOptions { TenantId = "acme" }), client, A.Fake<IAuthenticationService>());
        return (command, client, logger);
    }
}
