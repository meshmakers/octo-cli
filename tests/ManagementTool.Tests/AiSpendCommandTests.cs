using FakeItEasy;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Ai;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AiServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ManagementTool.Tests;

/// <summary>AB#8690 — SetAiSpendCap and GetAiSpend build the documented requests and refuse bad input locally.</summary>
public sealed class AiSpendCommandTests
{
    private readonly IAiServicesClient _client = A.Fake<IAiServicesClient>();

    private SetAiSpendCapCommand NewSet() =>
        new(NullLogger<SetAiSpendCapCommand>.Instance, Options.Create(new OctoToolOptions { TenantId = "octosystem" }),
            _client, A.Fake<IAuthenticationService>());

    private GetAiSpendCommand NewGet(IConsoleService console, string? tenant = "customer") =>
        new(NullLogger<GetAiSpendCommand>.Instance, console, Options.Create(new OctoToolOptions { TenantId = tenant }),
            _client, A.Fake<IAuthenticationService>());

    [Fact]
    public async Task SetAiSpendCap_SendsTenantCapCentralKeyAndReason()
    {
        SetAiSpendCapRequestDto? sent = null;
        // A tenant without an entry yet reads as key source None without a change date.
        A.CallTo(() => _client.GetAiSpendCapAsync("ai-sandbox"))
            .Returns(new AiSpendReportDto { TenantId = "ai-sandbox", KeySource = "None" });
        A.CallTo(() => _client.SetAiSpendCapAsync("ai-sandbox", A<SetAiSpendCapRequestDto>._))
            .Invokes((string _, SetAiSpendCapRequestDto r) => sent = r)
            .Returns(new AiSpendCapChangeDto { TenantId = "ai-sandbox", NewCapMonthlyEur = 12.5m });
        var command = NewSet();
        command.CommandArgumentValue.ParseLayer(["-t", "ai-sandbox", "-c", "12.5", "-r", "pilot budget"]);

        await command.Execute();

        Assert.NotNull(sent);
        Assert.Equal(12.5m, sent!.CapMonthlyEur);
        Assert.Equal("CentralKey", sent.KeySource);
        Assert.Equal("pilot budget", sent.Reason);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12,5")]
    public async Task SetAiSpendCap_InvalidCap_IsRefusedBeforeAnyRequest(string cap)
    {
        var command = NewSet();
        command.CommandArgumentValue.ParseLayer(["-t", "ai-sandbox", "-c", cap, "-r", "x"]);

        await Assert.ThrowsAsync<ToolException>(command.Execute);
        A.CallTo(() => _client.SetAiSpendCapAsync(A<string>._, A<SetAiSpendCapRequestDto>._)).MustNotHaveHappened();
    }

    [Theory]
    [InlineData("ByoKey", null, "ByoKey")]
    [InlineData("Subscription", null, "Subscription")]
    [InlineData("None", "2101-01-01T00:00:00Z", "None")] // deliberately blocked: stays blocked
    [InlineData("None", null, "CentralKey")] // no entry yet: the first cap opens the central key
    [InlineData("CentralKey", "2101-01-01T00:00:00Z", "CentralKey")]
    public async Task SetAiSpendCap_WithoutKeySource_KeepsTheStoredKeySource(string stored, string? changedAt,
        string expected)
    {
        SetAiSpendCapRequestDto? sent = null;
        A.CallTo(() => _client.GetAiSpendCapAsync("ai-sandbox")).Returns(new AiSpendReportDto
        {
            TenantId = "ai-sandbox",
            KeySource = stored,
            CapChangedAt = changedAt == null ? null : DateTimeOffset.Parse(changedAt, System.Globalization.CultureInfo.InvariantCulture)
        });
        A.CallTo(() => _client.SetAiSpendCapAsync("ai-sandbox", A<SetAiSpendCapRequestDto>._))
            .Invokes((string _, SetAiSpendCapRequestDto r) => sent = r)
            .Returns(new AiSpendCapChangeDto { TenantId = "ai-sandbox" });
        var command = NewSet();
        command.CommandArgumentValue.ParseLayer(["-t", "ai-sandbox", "-c", "5", "-r", "raise"]);

        await command.Execute();

        Assert.Equal(expected, sent!.KeySource);
    }

    [Theory]
    [InlineData("Free")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("3")]
    public async Task SetAiSpendCap_WithoutKeySource_UnknownStoredKeySource_IsRefused_AndNothingIsSet(string? stored)
    {
        // A stored value the CLI does not know must not become CentralKey on a cap-only change (fail closed).
        A.CallTo(() => _client.GetAiSpendCapAsync("ai-sandbox")).Returns(new AiSpendReportDto
        {
            TenantId = "ai-sandbox",
            KeySource = stored!,
            CapChangedAt = DateTimeOffset.UnixEpoch
        });
        var command = NewSet();
        command.CommandArgumentValue.ParseLayer(["-t", "ai-sandbox", "-c", "5", "-r", "raise"]);

        var refusal = await Assert.ThrowsAsync<ToolException>(command.Execute);
        Assert.Contains("-ks", refusal.Message);
        A.CallTo(() => _client.SetAiSpendCapAsync(A<string>._, A<SetAiSpendCapRequestDto>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task SetAiSpendCap_ExplicitKeySource_IsSentInCanonicalForm_WithoutReadingTheStoredOne()
    {
        SetAiSpendCapRequestDto? sent = null;
        A.CallTo(() => _client.SetAiSpendCapAsync("ai-sandbox", A<SetAiSpendCapRequestDto>._))
            .Invokes((string _, SetAiSpendCapRequestDto r) => sent = r)
            .Returns(new AiSpendCapChangeDto { TenantId = "ai-sandbox" });
        var command = NewSet();
        command.CommandArgumentValue.ParseLayer(["-t", "ai-sandbox", "-c", "5", "-r", "own key", "-ks", "byokey"]);

        await command.Execute();

        Assert.Equal("ByoKey", sent!.KeySource);
        A.CallTo(() => _client.GetAiSpendCapAsync(A<string>._)).MustNotHaveHappened();
    }

    [Theory]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task SetAiSpendCap_BlankOrTooLongReason_IsRefusedBeforeAnyRequest(string? reason)
    {
        var command = NewSet();
        command.CommandArgumentValue.ParseLayer(["-t", "ai-sandbox", "-c", "1", "-r", reason ?? new string('r', 501)]);

        await Assert.ThrowsAsync<ToolException>(command.Execute);
        A.CallTo(() => _client.SetAiSpendCapAsync(A<string>._, A<SetAiSpendCapRequestDto>._)).MustNotHaveHappened();
        A.CallTo(() => _client.GetAiSpendCapAsync(A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task SetAiSpendCap_UnknownKeySource_IsRefusedBeforeAnyRequest()
    {
        var command = NewSet();
        command.CommandArgumentValue.ParseLayer(["-t", "ai-sandbox", "-c", "1", "-r", "x", "-ks", "Free"]);

        await Assert.ThrowsAsync<ToolException>(command.Execute);
        A.CallTo(() => _client.SetAiSpendCapAsync(A<string>._, A<SetAiSpendCapRequestDto>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task GetAiSpend_WithoutTenant_ReadsTheOwnTenant_AndPrintsCapAndMonths()
    {
        var rows = new List<(string, string)>();
        var console = A.Fake<IConsoleService>();
        A.CallTo(() => console.WriteColumnLine(A<string>._, A<int>._, A<string>._))
            .Invokes((string c1, int _, string c2) => rows.Add((c1, c2)));
        A.CallTo(() => _client.GetAiSpendAsync()).Returns(new AiSpendReportDto
        {
            TenantId = "customer",
            KeySource = "CentralKey",
            CapMonthlyEur = 20m,
            Months =
            [
                new AiSpendMonthDto
                {
                    Month = "2026-11", SpentEur = 1.5m, ReservedEur = 0.25m, CallCount = 4,
                    ResetsAt = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero)
                }
            ]
        });
        var command = NewGet(console);
        command.CommandArgumentValue.ParseLayer([]);

        await command.Execute();

        A.CallTo(() => _client.GetAiSpendCapAsync(A<string>._)).MustNotHaveHappened();
        Assert.Contains(("Key source", "CentralKey"), rows);
        Assert.Contains(("Cap (EUR/month)", "20.00"), rows);
        Assert.Contains(("2026-11 spent", "1.50"), rows);
        Assert.Contains(("2026-11 calls", "4"), rows);
        Assert.Contains(("2026-11 resets", "2026-12-01 00:00 UTC"), rows);
    }

    [Fact]
    public async Task GetAiSpend_WithTenant_UsesTheCapAdministration()
    {
        A.CallTo(() => _client.GetAiSpendCapAsync("ai-sandbox")).Returns(new AiSpendReportDto { TenantId = "ai-sandbox" });
        var command = NewGet(A.Fake<IConsoleService>(), "octosystem");
        command.CommandArgumentValue.ParseLayer(["-t", "ai-sandbox"]);

        await command.Execute();

        A.CallTo(() => _client.GetAiSpendCapAsync("ai-sandbox")).MustHaveHappenedOnceExactly();
        A.CallTo(() => _client.GetAiSpendAsync()).MustNotHaveHappened();
    }
}
