using System.Security.Cryptography;
using System.Text;
using FakeItEasy;
using GraphQL;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.ApiSecrets;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace ManagementTool.Tests;

/// <summary>
///     The identity generates the secret itself: the create commands run the <c>AddSecret</c> method of the client or API
///     resource with only expiration and description (no secret in the request), print the generated clear text exactly
///     once - in the secret DTO the REST endpoint answered, with the stored value computed as the identity service stores
///     it - and keep both the clear text and the stored value out of the log, also when the command fails.
/// </summary>
public sealed class CreateApiSecretCommandsTests
{
    private const string ClearText = "server-generated-clear-text-secret";

    private static readonly string StoredValue =
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(ClearText)));

    private readonly IIdentityServicesClient _identity = A.Fake<IIdentityServicesClient>();
    private readonly IConsoleService _console = A.Fake<IConsoleService>();
    private readonly List<string> _written = [];
    private readonly List<GraphQLRequest> _requests = [];
    private readonly ITenantClient _tenant;

    public CreateApiSecretCommandsTests()
    {
        A.CallTo(() => _console.WriteLine(A<string>._)).Invokes((string s) => _written.Add(s));
        _tenant = MethodFakes.IdentityTenant(_requests, ClearText);
        MethodFakes.GiveClientsRuntimeIds(_identity);
    }

    [Fact]
    public async Task CreateForClient_SendsNoSecret_PrintsClearTextOnce_LogsNoSecret()
    {
        var logger = new RecordingLogger<CreateApiSecretClient>();
        var command = CreateForClient(logger, "-cid", "c1", "-d", "d");

        await command.Execute();

        AssertAddSecretWithoutSecret("d");
        AssertShownOnceAndNotLogged(logger.Entries);
        AssertNoRestWrite();
    }

    [Fact]
    public async Task CreateForApiResource_SendsNoSecret_PrintsClearTextOnce_LogsNoSecret()
    {
        var logger = new RecordingLogger<CreateApiSecretApiResource>();
        var command = new CreateApiSecretApiResource(logger,
            Options.Create(new OctoToolOptions { TenantId = "acme" }), _console, _identity, _tenant,
            A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(["-n", "res", "-d", "d"]);

        await command.Execute();

        AssertAddSecretWithoutSecret("d");
        AssertShownOnceAndNotLogged(logger.Entries);
        AssertNoRestWrite();
    }

    [Fact]
    public async Task CreateForClient_PublicClient_FailsWithTheDeclaredError_PrintsNothing()
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, "CLIENT_SECRET_NOT_REQUIRED",
            "The client is a public client and needs no secret.");
        var logger = new RecordingLogger<CreateApiSecretClient>();
        var command = CreateForClient(logger, "-cid", "c1");

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() => command.Execute());

        Assert.StartsWith("CLIENT_SECRET_NOT_REQUIRED: ", ex.Message);
        Assert.Empty(_written);
    }

    [Fact]
    public async Task CreateForClient_IdentityWithoutTheMethod_NamesTheVersion_AndDoesNotFallBack()
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, "METHOD_HANDLER_NOT_AVAILABLE");
        var logger = new RecordingLogger<CreateApiSecretClient>();
        var command = CreateForClient(logger, "-cid", "c1");

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() => command.Execute());

        Assert.Contains("System.Identity 3.8.0", ex.Message);
        Assert.Contains("Methods:Identity:EnablePasswordMethods", ex.Message);
        Assert.Contains("does not fall back to the REST endpoint", ex.Message);
        Assert.Empty(_written);
        AssertNoRestWrite();
    }

    private CreateApiSecretClient CreateForClient(RecordingLogger<CreateApiSecretClient> logger, params string[] args)
    {
        var command = new CreateApiSecretClient(logger, Options.Create(new OctoToolOptions { TenantId = "acme" }),
            _console, _identity, _tenant, A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(args);
        return command;
    }

    private void AssertAddSecretWithoutSecret(string description)
    {
        var request = Assert.Single(_requests);
        Assert.Contains("addSecret(", request.Query, StringComparison.Ordinal);
        var variables = MethodFakes.VariablesOf(request);
        Assert.Contains(description, variables);
        Assert.DoesNotContain(ClearText, variables);
        Assert.DoesNotContain(StoredValue, variables);
    }

    private void AssertShownOnceAndNotLogged(IEnumerable<LogEntry> entries)
    {
        var printed = Assert.Single(_written, w => w.Contains(ClearText));
        var dto = JsonConvert.DeserializeObject<ApiSecretDto>(printed)!;
        Assert.Equal(ClearText, dto.ValueClearText);
        Assert.Equal(StoredValue, dto.ValueEncrypted);
        Assert.Equal("d", dto.Description);
        Assert.DoesNotContain(entries, e => e.Message.Contains(ClearText) || e.Message.Contains(StoredValue));
    }

    private void AssertNoRestWrite() =>
        A.CallTo(_identity).Where(c => !c.Method.IsSpecialName && !c.Method.Name.StartsWith("Get", StringComparison.Ordinal))
            .MustNotHaveHappened();
}
