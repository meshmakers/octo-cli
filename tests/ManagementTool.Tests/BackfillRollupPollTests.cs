using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.TimeSeries;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Xunit;

namespace Meshmakers.Octo.Frontend.ManagementTool.Tests;

/// <summary>
/// Unit coverage for the BackfillRollup <c>--wait</c> poll loop (AB#4286). Uses a fake clock that
/// advances one poll interval per <c>delay</c> call so the loop terminates deterministically without
/// real time.
/// </summary>
public class BackfillRollupPollTests
{
    private const string JobId = "69fda707d47638c68edc7fec";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private static RollupRecomputeJobInfoDto Job(string state) =>
        new(JobId, state, 42, 3, DateTime.UtcNow, DateTime.UtcNow, 12, null);

    private sealed class FakeClock
    {
        private DateTime _now = new(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);
        public DateTime Now() => _now;
        public Task Advance(TimeSpan by)
        {
            _now += by;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Poll_ReturnsTerminal_WhenJobCompletes()
    {
        var clock = new FakeClock();
        var calls = 0;
        Task<IReadOnlyList<RollupRecomputeJobInfoDto>> ListJobs()
        {
            calls++;
            // Running for the first two polls, then Completed.
            var state = calls < 3 ? "Running" : "Completed";
            return Task.FromResult<IReadOnlyList<RollupRecomputeJobInfoDto>>(new[] { Job(state) });
        }

        var (result, job) = await BackfillRollupCommand.PollUntilTerminalAsync(
            ListJobs, JobId, clock.Now, clock.Advance, Interval, Timeout);

        Assert.Equal(BackfillRollupCommand.BackfillPollResult.Terminal, result);
        Assert.NotNull(job);
        Assert.Equal("Completed", job!.State);
    }

    [Fact]
    public async Task Poll_ReturnsTerminal_OnFailedState()
    {
        var clock = new FakeClock();
        Task<IReadOnlyList<RollupRecomputeJobInfoDto>> ListJobs() =>
            Task.FromResult<IReadOnlyList<RollupRecomputeJobInfoDto>>(new[] { Job("Failed") });

        var (result, job) = await BackfillRollupCommand.PollUntilTerminalAsync(
            ListJobs, JobId, clock.Now, clock.Advance, Interval, Timeout);

        Assert.Equal(BackfillRollupCommand.BackfillPollResult.Terminal, result);
        Assert.Equal("Failed", job!.State);
    }

    [Fact]
    public async Task Poll_ReturnsVanished_WhenJobNotListed()
    {
        var clock = new FakeClock();
        Task<IReadOnlyList<RollupRecomputeJobInfoDto>> ListJobs() =>
            Task.FromResult<IReadOnlyList<RollupRecomputeJobInfoDto>>(Array.Empty<RollupRecomputeJobInfoDto>());

        var (result, job) = await BackfillRollupCommand.PollUntilTerminalAsync(
            ListJobs, JobId, clock.Now, clock.Advance, Interval, Timeout);

        Assert.Equal(BackfillRollupCommand.BackfillPollResult.Vanished, result);
        Assert.Null(job);
    }

    [Fact]
    public async Task Poll_TimesOut_WhenJobStaysNonTerminal()
    {
        var clock = new FakeClock();
        Task<IReadOnlyList<RollupRecomputeJobInfoDto>> ListJobs() =>
            Task.FromResult<IReadOnlyList<RollupRecomputeJobInfoDto>>(new[] { Job("Running") });

        var (result, job) = await BackfillRollupCommand.PollUntilTerminalAsync(
            ListJobs, JobId, clock.Now, clock.Advance, Interval, Timeout);

        Assert.Equal(BackfillRollupCommand.BackfillPollResult.TimedOut, result);
        Assert.Null(job);
    }
}
