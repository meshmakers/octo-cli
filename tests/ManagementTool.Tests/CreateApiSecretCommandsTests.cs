using FakeItEasy;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.ApiSecrets;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
/// AB#6258 - the identity generates the secret itself, so the create commands send only expiration and
/// description (never a placeholder hash), print the server's clear text once, and keep both the clear text
/// and the hash out of the log.
/// </summary>
public sealed class CreateApiSecretCommandsTests
{
    private const string ClearText = "server-generated-clear-text-secret";
    private const string Hash = "c2VydmVyLXN0b3JlZC1oYXNo";

    private readonly IIdentityServicesClient _identity = A.Fake<IIdentityServicesClient>();
    private readonly IConsoleService _console = A.Fake<IConsoleService>();
    private readonly List<string> _written = [];

    public CreateApiSecretCommandsTests()
    {
        A.CallTo(() => _console.WriteLine(A<string>._)).Invokes((string s) => _written.Add(s));
    }

    private static readonly ApiSecretDto Result = new()
    {
        ValueEncrypted = Hash,
        ValueClearText = ClearText,
        Description = "d"
    };

    [Fact]
    public async Task CreateForClient_SendsNoHash_PrintsClearTextOnce_LogsNoSecret()
    {
        ApiSecretDto? sent = null;
        A.CallTo(() => _identity.CreateApiSecretForClient("c1", A<ApiSecretDto>._))
            .ReturnsLazily((string _, ApiSecretDto d) =>
            {
                sent = d;
                return Result;
            });
        var logger = new RecordingLogger<CreateApiSecretClient>();
        var command = new CreateApiSecretClient(logger, Options.Create(new OctoToolOptions { TenantId = "acme" }),
            _console, _identity, A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(["-cid", "c1", "-d", "d"]);

        await command.Execute();

        Assert.NotNull(sent);
        Assert.Null(sent!.ValueEncrypted);
        Assert.Null(sent.ValueClearText);
        Assert.Equal("d", sent.Description);
        AssertShownOnceAndNotLogged(logger.Entries);
    }

    [Fact]
    public async Task CreateForApiResource_SendsNoHash_PrintsClearTextOnce_LogsNoSecret()
    {
        ApiSecretDto? sent = null;
        A.CallTo(() => _identity.CreateApiSecretForApiResource("res", A<ApiSecretDto>._))
            .ReturnsLazily((string _, ApiSecretDto d) =>
            {
                sent = d;
                return Result;
            });
        var logger = new RecordingLogger<CreateApiSecretApiResource>();
        var command = new CreateApiSecretApiResource(logger,
            Options.Create(new OctoToolOptions { TenantId = "acme" }), _console, _identity,
            A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(["-n", "res", "-d", "d"]);

        await command.Execute();

        Assert.NotNull(sent);
        Assert.Null(sent!.ValueEncrypted);
        AssertShownOnceAndNotLogged(logger.Entries);
    }

    private void AssertShownOnceAndNotLogged(IEnumerable<LogEntry> entries)
    {
        Assert.Single(_written, w => w.Contains(ClearText));
        Assert.DoesNotContain(entries, e => e.Message.Contains(ClearText) || e.Message.Contains(Hash));
    }
}
