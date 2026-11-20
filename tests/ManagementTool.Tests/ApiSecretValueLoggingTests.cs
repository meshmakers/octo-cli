using FakeItEasy;
using GraphQL;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.ApiSecrets;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
/// AB#6692 - the Delete/Update ApiSecret commands take the secret value as an argument and must never
/// write it to the log or the console (they print a <see cref="SecretMasker.Mask" /> placeholder instead).
/// </summary>
public sealed class ApiSecretValueLoggingTests
{
    private const string FakeSecret = "fake-secret-value-DO-NOT-LOG-0123456789";

    private readonly IIdentityServicesClient _identity = A.Fake<IIdentityServicesClient>();
    private readonly IConsoleService _console = A.Fake<IConsoleService>();
    private readonly IAuthenticationService _auth = A.Fake<IAuthenticationService>();
    private readonly IConfirmationService _confirm = A.Fake<IConfirmationService>();
    private readonly IOptions<OctoToolOptions> _options = Options.Create(new OctoToolOptions { TenantId = "acme" });
    private readonly List<string> _written = [];
    private readonly List<GraphQLRequest> _requests = [];
    private readonly ITenantClient _tenant;

    public ApiSecretValueLoggingTests()
    {
        A.CallTo(() => _console.WriteLine(A<string>._)).Invokes((string s) => _written.Add(s));
        A.CallTo(() => _confirm.Confirm(A<string>._)).Returns(true);
        _tenant = MethodFakes.IdentityTenant(_requests);
        MethodFakes.GiveClientsRuntimeIds(_identity);
    }

    /// <summary>The secret travels only in the variables of the one method call, never in the document.</summary>
    private void AssertSecretOnlyInTheMethodCall(string field)
    {
        var request = Assert.Single(_requests);
        Assert.Contains(field, request.Query, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeSecret, request.Query, StringComparison.Ordinal);
        Assert.Contains(FakeSecret, MethodFakes.VariablesOf(request), StringComparison.Ordinal);
    }

    private void AssertNoSecret(IEnumerable<LogEntry> entries)
    {
        Assert.NotEmpty(entries);
        Assert.DoesNotContain(entries, e => e.Message.Contains(FakeSecret));
        Assert.DoesNotContain(_written, w => w.Contains(FakeSecret));
    }

    [Fact]
    public async Task DeleteForClient_LogsNoSecret_ButCallsServiceWithIt()
    {
        var logger = new RecordingLogger<DeleteApiSecretClient>();
        var command = new DeleteApiSecretClient(logger, _options, _identity, _tenant, _auth, _confirm);
        command.CommandArgumentValue.ParseLayer(["-cid", "c1", "-s", FakeSecret, "-y"]);

        await command.Execute();

        AssertSecretOnlyInTheMethodCall("removeSecret");
        AssertNoSecret(logger.Entries);
    }

    [Fact]
    public async Task DeleteForApiResource_LogsNoSecret_ButCallsServiceWithIt()
    {
        var logger = new RecordingLogger<DeleteApiSecretApiResource>();
        var command = new DeleteApiSecretApiResource(logger, _options, _identity, _tenant, _auth, _confirm);
        command.CommandArgumentValue.ParseLayer(["-n", "res", "-s", FakeSecret, "-y"]);

        await command.Execute();

        AssertSecretOnlyInTheMethodCall("removeSecret");
        AssertNoSecret(logger.Entries);
    }

    [Fact]
    public async Task UpdateForClient_Found_LogsNoSecret()
    {
        A.CallTo(() => _identity.GetApiSecretForClient("c1", FakeSecret)).Returns(new ApiSecretDto { ValueEncrypted = FakeSecret });
        var logger = new RecordingLogger<UpdateApiSecretClient>();
        var command = new UpdateApiSecretClient(logger, _options, _identity, _tenant, _auth);
        command.CommandArgumentValue.ParseLayer(["-cid", "c1", "-s", FakeSecret, "-d", "new"]);

        await command.Execute();

        AssertSecretOnlyInTheMethodCall("updateSecret");
        AssertNoSecret(logger.Entries);
    }

    [Fact]
    public async Task UpdateForClient_NotFound_LogsNoSecret()
    {
        A.CallTo(() => _identity.GetApiSecretForClient("c1", FakeSecret)).Returns(Task.FromResult<ApiSecretDto>(null!));
        var logger = new RecordingLogger<UpdateApiSecretClient>();
        var command = new UpdateApiSecretClient(logger, _options, _identity, _tenant, _auth);
        command.CommandArgumentValue.ParseLayer(["-cid", "c1", "-s", FakeSecret]);

        await command.Execute();

        Assert.Contains(logger.Entries, e => e.Message.Contains("not found"));
        AssertNoSecret(logger.Entries);
    }

    [Fact]
    public async Task UpdateForApiResource_Found_LogsNoSecret()
    {
        A.CallTo(() => _identity.GetApiSecretForApiResource("res", FakeSecret)).Returns(new ApiSecretDto { ValueEncrypted = FakeSecret });
        var logger = new RecordingLogger<UpdateApiSecretApiResource>();
        var command = new UpdateApiSecretApiResource(logger, _options, _identity, _tenant, _auth);
        command.CommandArgumentValue.ParseLayer(["-n", "res", "-s", FakeSecret, "-d", "new"]);

        await command.Execute();

        AssertSecretOnlyInTheMethodCall("updateSecret");
        AssertNoSecret(logger.Entries);
    }

    [Fact]
    public async Task UpdateForApiResource_NotFound_LogsNoSecret()
    {
        A.CallTo(() => _identity.GetApiSecretForApiResource("res", FakeSecret)).Returns(Task.FromResult<ApiSecretDto>(null!));
        var logger = new RecordingLogger<UpdateApiSecretApiResource>();
        var command = new UpdateApiSecretApiResource(logger, _options, _identity, _tenant, _auth);
        command.CommandArgumentValue.ParseLayer(["-n", "res", "-s", FakeSecret]);

        await command.Execute();

        Assert.Contains(logger.Entries, e => e.Message.Contains("not found"));
        AssertNoSecret(logger.Entries);
    }

    [Fact]
    public async Task GetForClient_PrintsHashIdentifierOnly_NotInLog()
    {
        const string hash = "c3RvcmVkLWhhc2g=";
        A.CallTo(() => _identity.GetApiSecretsForClient("c1"))
            .Returns(new List<ApiSecretDto> { new() { ValueEncrypted = hash, Description = "d" } });
        var logger = new RecordingLogger<GetApiSecretsClient>();
        var command = new GetApiSecretsClient(logger, _options, _console, _identity, _auth);
        command.CommandArgumentValue.ParseLayer(["-cid", "c1"]);

        await command.Execute();

        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains(hash));
        Assert.Contains(_written, w => w.Contains(hash));
    }
}
