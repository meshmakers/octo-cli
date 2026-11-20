using FakeItEasy;
using GraphQL;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.AdminProvisioning;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     A mapping is unique per (source tenant, source user). <c>CreateAdminProvisioningMapping</c> runs the CK method
///     <c>ExternalTenantUserMapping.ProvisionAdmin-1</c>; a second create is its declared error <c>MAPPING_ALREADY_EXISTS</c>
///     with the parameter <c>existingMappingId</c>. These tests pin what the operator sees and gets as exit code: the
///     command has no catch of its own, and the real <see cref="Runner" /> prints the code, the message and the existing
///     mapping id, exit code -3.
/// </summary>
public sealed class CreateAdminProvisioningMappingConflictTests
{
    private const string ExistingId = "6ad4f62c1f0e4a3b9c8d7e6f";

    private const string Message =
        "A mapping for this source user already exists. Update its roles instead of creating a second mapping.";

    private readonly IIdentityServicesClient _identity = A.Fake<IIdentityServicesClient>();
    private readonly ITenantClient _tenant = A.Fake<ITenantClient>();
    private readonly List<GraphQLRequest> _requests = [];

    public CreateAdminProvisioningMappingConflictTests()
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, "MAPPING_ALREADY_EXISTS", Message,
            new Dictionary<string, object?> { ["existingMappingId"] = ExistingId });
    }

    private CreateAdminProvisioningMapping CreateCommand()
    {
        var command = new CreateAdminProvisioningMapping(
            new RecordingLogger<CreateAdminProvisioningMapping>(),
            Options.Create(new OctoToolOptions { TenantId = "octosystem" }),
            _identity, _tenant, A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(
            ["-ttid", "customer-project", "-stid", "octosystem", "-suid", "u-1", "-sun", "alice", "-rids", "Development"]);
        return command;
    }

    [Fact]
    public async Task DuplicateCreate_FailsWithTheDeclaredErrorAndTheExistingMapping_NotSwallowedByTheCommand()
    {
        var command = CreateCommand();

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() => command.Execute());

        Assert.Equal($"MAPPING_ALREADY_EXISTS: {Message} (existingMappingId={ExistingId})", ex.Message);
        var request = Assert.Single(_requests);
        Assert.Contains("provisionAdmin(", request.Query);
        var variables = MethodFakes.VariablesOf(request);
        Assert.Contains("\"targetTenantId\":\"customer-project\"", variables);
        Assert.Contains("\"sourceUserId\":\"u-1\"", variables);
        A.CallTo(() => _identity.CreateAdminProvisioningMapping(A<string>._, A<Meshmakers.Octo.Communication.Contracts.DataTransferObjects.CreateExternalTenantUserMappingDto>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task DuplicateCreate_RunnerPrintsTheDeclaredError_AndExitsWithMinusThree()
    {
        var command = CreateCommand();
        var parser = A.Fake<ICommandParser>();
        A.CallTo(() => parser.ParseAndValidateAsync(A<string?>._)).Returns(Task.Run(command.Execute, TestContext.Current.CancellationToken));
        var contexts = A.Fake<IContextManager>();
        A.CallTo(() => contexts.GetEffectiveContextName()).Returns("test");
        var logger = new RecordingLogger<Runner>();
        var runner = new Runner(logger, parser, contexts, ContextSelection.None);

        var exitCode = await runner.DoActionAsync();

        Assert.Equal(-3, exitCode);
        var error = Assert.Single(logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error);
        Assert.Contains("MAPPING_ALREADY_EXISTS", error.Message);
        Assert.Contains(ExistingId, error.Message);
        Assert.Contains("Update its roles", error.Message);
    }
}
