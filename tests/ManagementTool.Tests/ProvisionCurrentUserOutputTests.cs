using FakeItEasy;
using GraphQL;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.AdminProvisioning;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     ProvisionCurrentUser names the identity the mapping was written for. From a context scoped to an intermediate
///     tenant the identity service provisions the home identity of the shadow user, so the operator must see which source
///     tenant and user now reach the target. The command runs <c>ExternalTenantUserMapping.ProvisionCurrentUser-1</c>; the
///     names come from the method's result record.
/// </summary>
public sealed class ProvisionCurrentUserOutputTests
{
    private readonly ITenantClient _tenant = A.Fake<ITenantClient>();
    private readonly List<GraphQLRequest> _requests = [];

    private (ProvisionCurrentUser Command, RecordingLogger<ProvisionCurrentUser> Logger) CreateCommand()
    {
        var logger = new RecordingLogger<ProvisionCurrentUser>();
        var command = new ProvisionCurrentUser(logger, Options.Create(new OctoToolOptions { TenantId = "tmp-a" }),
            A.Fake<IIdentityServicesClient>(), _tenant, A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(["-ttid", "tmp-b"]);
        return (command, logger);
    }

    [Fact]
    public async Task Provision_NamesTheSourceTenantAndUserTheMappingWasWrittenFor()
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, null, result: new RtAdminProvisioningResultRecordDto
        {
            MappingRtId = "6a5f0c0ffee0000000000001", SourceTenantId = "octosystem",
            SourceUserId = "686a6215fd3e5972ff5693fd", SourceUserName = "alice@example.com", Outcome = "created"
        });
        var (command, logger) = CreateCommand();

        await command.Execute();

        var request = Assert.Single(_requests);
        Assert.Contains("provisionCurrentUser(", request.Query);
        Assert.Contains("\"targetTenantId\":\"tmp-b\"", MethodFakes.VariablesOf(request));
        var line = Assert.Single(logger.Entries,
            e => e.Level == LogLevel.Information && e.Message.StartsWith("Current user provisioned"));
        Assert.Contains("'tmp-b'", line.Message);
        Assert.Contains("'alice@example.com'", line.Message);
        Assert.Contains("686a6215fd3e5972ff5693fd", line.Message);
        Assert.Contains("tenant 'octosystem'", line.Message);
    }

    [Fact]
    public async Task Provision_WithoutResultRecord_StillReportsSuccess()
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, null);
        var (command, logger) = CreateCommand();

        await command.Execute();

        Assert.Contains(logger.Entries, e => e.Message == "Current user provisioned in target tenant 'tmp-b'");
    }
}
