using FakeItEasy;
using GraphQL;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.AdminProvisioning;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.ApiSecrets;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.ApiResources;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.ApiScopes;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.DataPermissions;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.EmailDomainGroupRules;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.ExternalTenantUserMappings;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Groups;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.IdentityProviders;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Roles;
using Meshmakers.Octo.Frontend.ManagementTool.Commands;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.Methods;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     The identity administration commands of groups, client roles, overlays and mirrors, roles, API scopes and
///     resources, data permissions and policies, e-mail domain group rules, external tenant user mappings, admin
///     provisioning and identity provider deletion run the CK methods of <c>System.Identity</c> through the method clients
///     generated in GraphQlDtos (<c>System.Identity-[3.8,4.0)</c>): every command sends its method mutation addressed by
///     runtime id and calls no REST write; a declared error ends it with code and message (exit -3), a platform refusal
///     (<c>METHOD_FORBIDDEN</c>) as well, without a REST write; the declared errors the REST endpoint answered with success
///     stay a success; a server without the methods ends the command with a message naming the needed identity version
///     (no REST fallback); the output is the one of the REST path.
/// </summary>
public class IdentityWave2MethodCommandTests
{
    private const string GroupRtId = "65f0c0ffee00000000000001";
    private const string ChildGroupRtId = "65f0c0ffee00000000000002";
    private const string UserRtId = "65f0c0ffee00000000000003";
    private const string ClientRtId = "65f0c0ffee00000000000004";
    private const string RoleRtId = "65f0c0ffee00000000000005";
    private const string ScopeRtId = "65f0c0ffee00000000000006";
    private const string ResourceRtId = "65f0c0ffee00000000000007";
    private const string PermissionRtId = "65f0c0ffee00000000000008";
    private const string PolicyRtId = "65f0c0ffee00000000000009";
    private const string RuleRtId = "65f0c0ffee0000000000000a";
    private const string MappingRtId = "65f0c0ffee0000000000000b";
    private const string ProviderRtId = "65f0c0ffee0000000000000c";
    private const string ReaderRoleRtId = "65f0c0ffee0000000000000d";

    private readonly IIdentityServicesClient _identity = A.Fake<IIdentityServicesClient>();
    private readonly ITenantClient _tenant = A.Fake<ITenantClient>();
    private readonly IAuthenticationService _auth = A.Fake<IAuthenticationService>();
    private readonly IConfirmationService _confirmation = A.Fake<IConfirmationService>();
    private readonly List<string> _consoleLines = [];
    private readonly List<GraphQLRequest> _requests = [];

    /// <summary>Command, arguments, the GraphQL field of its method and the runtime id the call must carry.</summary>
    public static readonly Dictionary<string, (Type Command, string[] Args, string Field, string RtId)> Cases = new()
    {
        ["AddUserToGroup"] = (typeof(AddUserToGroup), ["-id", GroupRtId, "-uid", UserRtId], "addMember(", GroupRtId),
        ["RemoveUserFromGroup"] = (typeof(RemoveUserFromGroup), ["-id", GroupRtId, "-uid", UserRtId], "removeMember(", GroupRtId),
        ["AddClientToGroup"] = (typeof(AddClientToGroup), ["-id", GroupRtId, "-cid", "ci-client"], "addMember(", GroupRtId),
        ["RemoveClientFromGroup"] = (typeof(RemoveClientFromGroup), ["-id", GroupRtId, "-cid", "ci-client"], "removeMember(", GroupRtId),
        ["AddGroupToGroup"] = (typeof(AddGroupToGroup), ["-id", GroupRtId, "-cgid", ChildGroupRtId], "addChildGroup(", GroupRtId),
        ["RemoveGroupFromGroup"] = (typeof(RemoveGroupFromGroup), ["-id", GroupRtId, "-cgid", ChildGroupRtId], "removeChildGroup(", GroupRtId),
        ["CreateGroup"] = (typeof(CreateGroup), ["-n", "operators", "-rids", ReaderRoleRtId], "createGroup(", "reader"),
        ["UpdateGroup"] = (typeof(UpdateGroup), ["-id", GroupRtId, "-n", "operators"], "updateGroup(", GroupRtId),
        ["DeleteGroup"] = (typeof(DeleteGroup), ["-id", GroupRtId], "deleteGroup(", GroupRtId),
        ["UpdateGroupRoles"] = (typeof(UpdateGroupRoles), ["-id", GroupRtId, "-rids", ReaderRoleRtId], "setRoles(", GroupRtId),
        ["AddClientToRole"] = (typeof(AddClientToRole), ["-id", "ci-client", "-r", "reader"], "addRole(", ClientRtId),
        ["RemoveClientFromRole"] = (typeof(RemoveClientFromRole), ["-id", "ci-client", "-r", "reader", "-y"], "removeRole(", ClientRtId),
        ["UpdateClientRoles"] = (typeof(UpdateClientRoles), ["-id", "ci-client", "-rids", ReaderRoleRtId], "setRoles(", ClientRtId),
        ["DeleteClient"] = (typeof(DeleteClient), ["-id", "ci-client", "-y"], "deleteClient(", ClientRtId),
        ["ApplyClientOverlay"] = (typeof(ApplyClientOverlay), ["-id", "ci-client", "-n", "dev", "-r", "https://localhost/cb"], "applyOverlayUris(", ClientRtId),
        ["CleanClientOverlays"] = (typeof(CleanClientOverlays), ["-n", "dev", "-y"], "cleanOverlayEntries(", "dev"),
        ["ProvisionClientInExistingTenants"] = (typeof(ProvisionClientInExistingTenants), ["-id", "ci-client"], "provisionMirrors(", ClientRtId),
        ["ProvisionClientInTenant"] = (typeof(ProvisionClientInTenant), ["-id", "ci-client", "-ctid", "child-a"], "provisionMirrorInTenant(", ClientRtId),
        ["UnprovisionClientFromTenant"] = (typeof(UnprovisionClientFromTenant), ["-id", "ci-client", "-ctid", "child-a", "-y"], "removeMirror(", ClientRtId),
        ["SetClientAutoProvision"] = (typeof(SetClientAutoProvision), ["-id", "ci-client", "-e", "true"], "setMirrorAutoProvision(", ClientRtId),
        ["CreateRole"] = (typeof(CreateRole), ["-n", "operator"], "createRole(", "operator"),
        ["UpdateRole"] = (typeof(UpdateRole), ["-n", "operator", "-nn", "operators"], "updateRole(", RoleRtId),
        ["DeleteRole"] = (typeof(DeleteRole), ["-n", "operator", "-y"], "deleteRole(", RoleRtId),
        ["CreateApiScope"] = (typeof(CreateApiScope), ["-n", "telemetry"], "createApiScope(", "telemetry"),
        ["UpdateApiScope"] = (typeof(UpdateApiScope), ["-n", "telemetry", "-dn", "Telemetry"], "updateApiScope(", ScopeRtId),
        ["DeleteApiScope"] = (typeof(DeleteApiScope), ["-n", "telemetry", "-y"], "deleteApiScope(", ScopeRtId),
        ["CreateApiResource"] = (typeof(CreateApiResource), ["-n", "telemetryApi", "-s", "telemetry"], "createApiResource(", "telemetryApi"),
        ["UpdateApiResource"] = (typeof(UpdateApiResource), ["-n", "telemetryApi", "-s", "telemetry"], "updateApiResource(", ResourceRtId),
        ["DeleteApiResource"] = (typeof(DeleteApiResource), ["-n", "telemetryApi", "-y"], "deleteApiResource(", ResourceRtId),
        ["CreateDataPermission"] = (typeof(CreateDataPermission), ["-pid", "plant-a"], "createDataPermission(", "plant-a"),
        ["DeleteDataPermission"] = (typeof(DeleteDataPermission), ["-pid", "plant-a", "-y"], "deleteDataPermission(", PermissionRtId),
        ["CreateDataPolicy"] = (typeof(CreateDataPolicy), ["-pid", "plant-a", "-t", "Basic/Asset", "-a", "Read"], "createPolicy(", PermissionRtId),
        ["GrantDataPermission"] = (typeof(GrantDataPermission), ["-pid", "plant-a", "-r", "reader"], "grantToRole(", PermissionRtId),
        ["RevokeDataPermission"] = (typeof(RevokeDataPermission), ["-pid", "plant-a", "-r", "reader"], "revokeFromRole(", PermissionRtId),
        ["SetDataPolicyEnforcementMode"] = (typeof(SetDataPolicyEnforcementMode), ["-id", PolicyRtId, "-m", "AuditOnly"], "setEnforcementMode(", PolicyRtId),
        ["CreateEmailDomainGroupRule"] = (typeof(CreateEmailDomainGroupRule), ["-edp", "*@example.com", "-tgid", GroupRtId], "createEmailDomainGroupRule(", GroupRtId),
        ["UpdateEmailDomainGroupRule"] = (typeof(UpdateEmailDomainGroupRule), ["-id", RuleRtId, "-edp", "*@example.com", "-tgid", GroupRtId], "updateEmailDomainGroupRule(", RuleRtId),
        ["DeleteEmailDomainGroupRule"] = (typeof(DeleteEmailDomainGroupRule), ["-id", RuleRtId], "deleteEmailDomainGroupRule(", RuleRtId),
        ["CreateExternalTenantUserMapping"] = (typeof(CreateExternalTenantUserMapping), ["-stid", "parent", "-suid", UserRtId, "-sun", "alice"], "createMapping(", UserRtId),
        ["UpdateExternalTenantUserMapping"] = (typeof(UpdateExternalTenantUserMapping), ["-id", MappingRtId, "-rids", ReaderRoleRtId], "updateMapping(", MappingRtId),
        ["DeleteExternalTenantUserMapping"] = (typeof(DeleteExternalTenantUserMapping), ["-id", MappingRtId], "deleteMapping(", MappingRtId),
        ["CreateAdminProvisioningMapping"] = (typeof(CreateAdminProvisioningMapping), ["-ttid", "child-a", "-stid", "parent", "-suid", UserRtId, "-sun", "alice"], "provisionAdmin(", "child-a"),
        ["DeleteAdminProvisioningMapping"] = (typeof(DeleteAdminProvisioningMapping), ["-ttid", "child-a", "-mid", MappingRtId], "removeAdminProvisioning(", MappingRtId),
        ["ProvisionCurrentUser"] = (typeof(ProvisionCurrentUser), ["-ttid", "child-a"], "provisionCurrentUser(", "child-a"),
        ["DeleteIdentityProvider"] = (typeof(DeleteIdentityProvider), ["-id", ProviderRtId, "-y"], "deleteIdentityProvider(", ProviderRtId),
        ["UpdateIdentityProvider"] = (typeof(UpdateIdentityProvider), ["-id", ProviderRtId, "-n", "google", "-e", "true"], "updateIdentityProvider(", ProviderRtId),
        ["UpdateClient"] = (typeof(UpdateClient), ["-id", "ci-client", "-n", "CI"], "updateClient(", ClientRtId),
        ["AddScopeToClient"] = (typeof(AddScopeToClient), ["-id", "ci-client", "-n", "telemetry"], "updateClient(", ClientRtId),
        ["DeleteApiSecretClient"] = (typeof(DeleteApiSecretClient), ["-cid", "ci-client", "-s", "stored-value", "-y"], "removeSecret(", ClientRtId),
        ["DeleteApiSecretApiResource"] = (typeof(DeleteApiSecretApiResource), ["-n", "telemetryApi", "-s", "stored-value", "-y"], "removeSecret(", ResourceRtId),
        ["UpdateApiSecretClient"] = (typeof(UpdateApiSecretClient), ["-cid", "ci-client", "-s", "stored-value", "-d", "new"], "updateSecret(", ClientRtId),
        ["UpdateApiSecretApiResource"] = (typeof(UpdateApiSecretApiResource), ["-n", "telemetryApi", "-s", "stored-value", "-d", "new"], "updateSecret(", ResourceRtId),
    };

    public static TheoryData<string> CaseNames => new(Cases.Keys);

    public IdentityWave2MethodCommandTests()
    {
        A.CallTo(() => _identity.GetClient(A<string>._)).ReturnsLazily((string id) =>
            Task.FromResult(new ClientDto { ClientId = id, RtId = ClientRtId }));
        A.CallTo(() => _identity.GetRoles()).Returns(Task.FromResult<IEnumerable<RoleDto>>(
            [new RoleDto { Id = ReaderRoleRtId, Name = "reader" }]));
        A.CallTo(() => _identity.GetRoleByName(A<string>._)).ReturnsLazily((string name) =>
            Task.FromResult(new RoleDto { Id = RoleRtId, Name = name }));
        A.CallTo(() => _identity.GetDataPermissions()).Returns(Task.FromResult<IEnumerable<DataPermissionDto>>(
            [new DataPermissionDto { Id = OctoObjectId.Parse(PermissionRtId), PermissionId = "plant-a" }]));
        A.CallTo(() => _identity.GetIdentityProvider(A<OctoObjectId>._)).Returns(Task.FromResult<IdentityProviderDto>(
            new GoogleIdentityProviderDto { RtId = OctoObjectId.Parse(ProviderRtId), Name = "google" }));
        A.CallTo(() => _identity.GetApiScope(A<string>._)).ReturnsLazily((string name) =>
            Task.FromResult(new ApiScopeDto { Name = name, IsEnabled = true }));
        A.CallTo(() => _tenant.SendQueryAsync<RtApiScopeDto>(A<GraphQLRequest>._)).Returns(
            Task.FromResult<QlItemsContainer<RtApiScopeDto>?>(new QlItemsContainer<RtApiScopeDto>
                { Items = [new RtApiScopeDto { RtId = OctoObjectId.Parse(ScopeRtId) }] }));
        A.CallTo(() => _tenant.SendQueryAsync<RtApiResourceDto>(A<GraphQLRequest>._)).Returns(
            Task.FromResult<QlItemsContainer<RtApiResourceDto>?>(new QlItemsContainer<RtApiResourceDto>
                { Items = [new RtApiResourceDto { RtId = OctoObjectId.Parse(ResourceRtId) }] }));
        A.CallTo(() => _confirmation.Confirm(A<string>._)).Returns(true);
        A.CallTo(() => _identity.GetApiSecretForClient(A<string>._, A<string>._)).Returns(
            Task.FromResult(new ApiSecretDto { ValueEncrypted = "stored-value" }));
        A.CallTo(() => _identity.GetApiSecretForApiResource(A<string>._, A<string>._)).Returns(
            Task.FromResult(new ApiSecretDto { ValueEncrypted = "stored-value" }));
        A.CallTo(() => _identity.ApplyClientOverlay(A<string>._, A<ApplyOverlayUrisDto>._)).Returns(
            Task.FromResult(new ApplyOverlayUrisResultDto
            {
                OverlayName = "dev", ClientId = "ci-client", RedirectUris = new ApplyOverlayUrisListCountDto(),
                PostLogoutRedirectUris = new ApplyOverlayUrisListCountDto(),
                AllowedCorsOrigins = new ApplyOverlayUrisListCountDto()
            }));
        A.CallTo(() => _identity.CleanOverlayEntries(A<string?>._)).Returns(
            Task.FromResult(new CleanOverlayEntriesResultDto { ClientResults = [] }));
        MethodFakes.AnswerMethodCalls(_tenant, _requests, null);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task Command_RunsItsMethod_ByRuntimeId_AndCallsNoRestWrite(string name)
    {
        var (type, args, field, rtId) = Cases[name];
        var command = Create(type, args);

        await command.Execute();

        var request = Assert.Single(_requests);
        Assert.Contains(field, request.Query, StringComparison.Ordinal);
        Assert.Contains(rtId, Variables(request), StringComparison.Ordinal);
        A.CallTo(() => _auth.EnsureAuthenticated(_tenant.AccessToken)).MustHaveHappened();
        AssertNoRestWrite();
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task Command_RefusedByTheMethodGateway_FailsWithTheCode_AndCallsNoRestWrite(string name)
    {
        var (type, args, _, _) = Cases[name];
        MethodFakes.AnswerMethodCalls(_tenant, _requests, "METHOD_FORBIDDEN",
            parameters: new Dictionary<string, object?> { ["reason"] = "tenant-not-allowed" });
        var command = Create(type, args);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => command.Execute());

        // Commands that send a secret value rethrow the refusal as a scrubbed ServiceClientException with the same line.
        var text = ex is CkMethodCallException call ? CkMethodErrorText.Format(call) : ex.Message;
        Assert.True(ex is CkMethodCallException or ServiceClientException, ex.GetType().Name);
        Assert.Contains("METHOD_FORBIDDEN", text);
        Assert.Contains("reason=tenant-not-allowed", text);
        AssertNoRestWrite();
    }

    [Fact]
    public async Task ARefusedCall_ThroughTheRunner_PrintsCodeAndParameters_AndExitsWithMinusThree()
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, "METHOD_FORBIDDEN",
            parameters: new Dictionary<string, object?> { ["reason"] = "role" });
        var command = Create(typeof(DeleteGroup), ["-id", GroupRtId]);
        var parser = A.Fake<ICommandParser>();
        A.CallTo(() => parser.ParseAndValidateAsync(A<string?>._))
            .Returns(Task.Run(command.Execute, TestContext.Current.CancellationToken));
        var contexts = A.Fake<IContextManager>();
        A.CallTo(() => contexts.GetEffectiveContextName()).Returns("test");
        var logger = new RecordingLogger<Runner>();

        var exitCode = await new Runner(logger, parser, contexts, ContextSelection.None).DoActionAsync();

        Assert.Equal(-3, exitCode);
        var error = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains("METHOD_FORBIDDEN", error.Message);
        Assert.Contains("reason=role", error.Message);
        A.CallTo(() => _identity.DeleteGroup(A<OctoObjectId>._)).MustNotHaveHappened();
    }

    [Theory]
    [InlineData("DeleteApiSecretClient")]
    [InlineData("UpdateApiSecretApiResource")]
    public async Task ARefusalThatEchoesTheSecretValue_IsScrubbed(string name)
    {
        var (type, args, _, _) = Cases[name];
        MethodFakes.AnswerMethodCalls(_tenant, _requests, "METHOD_FORBIDDEN", "Refused for stored-value",
            new Dictionary<string, object?> { ["value"] = "stored-value" });

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() => Create(type, args).Execute());

        Assert.DoesNotContain("stored-value", ex.Message);
        Assert.Contains("[REDACTED]", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task DeclaredError_FailsWithCodeMessageAndParameters()
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, "GROUP_CYCLE", "The group would contain itself.",
            new Dictionary<string, object?> { ["cycle"] = "descendant" });
        var command = Create(typeof(AddGroupToGroup), ["-id", GroupRtId, "-cgid", ChildGroupRtId]);

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() => command.Execute());

        Assert.Equal("GROUP_CYCLE: The group would contain itself. (cycle=descendant)", ex.Message);
        AssertNoRestWrite();
    }

    [Theory]
    [InlineData("AddUserToGroup", "MEMBER_ALREADY_IN_GROUP")]
    [InlineData("RemoveUserFromGroup", "MEMBER_NOT_IN_GROUP")]
    [InlineData("RemoveUserFromGroup", "MEMBER_NOT_FOUND")]
    [InlineData("AddClientToGroup", "MEMBER_ALREADY_IN_GROUP")]
    [InlineData("RemoveClientFromGroup", "MEMBER_NOT_IN_GROUP")]
    [InlineData("AddGroupToGroup", "CHILD_GROUP_ALREADY_IN_GROUP")]
    [InlineData("RemoveGroupFromGroup", "CHILD_GROUP_NOT_IN_GROUP")]
    [InlineData("RemoveClientFromRole", "ROLE_NOT_FOUND")]
    [InlineData("GrantDataPermission", "PERMISSION_ALREADY_GRANTED")]
    [InlineData("RevokeDataPermission", "PERMISSION_NOT_GRANTED")]
    public async Task DeclaredErrorsTheRestEndpointAnsweredWithSuccess_StayASuccess(string name, string code)
    {
        var (type, args, _, _) = Cases[name];
        MethodFakes.AnswerMethodCalls(_tenant, _requests, code);
        var command = Create(type, args);

        await command.Execute();

        Assert.Single(_requests);
        AssertNoRestWrite();
    }

    [Fact]
    public async Task RemoveClientFromGroup_UnknownClientId_FailsAsTheRestEndpointDid()
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, "MEMBER_NOT_FOUND", "Member not found.");
        var command = Create(typeof(RemoveClientFromGroup), ["-id", GroupRtId, "-cid", "no-such-client"]);

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() => command.Execute());

        Assert.StartsWith("MEMBER_NOT_FOUND: ", ex.Message);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task ServerWithoutTheMethods_NamesTheNeededVersion_AndCallsNoRestWrite(string name)
    {
        var (type, args, _, _) = Cases[name];
        MethodFakes.AnswerMethodCallsWithValidationErrors(_tenant, _requests, "SystemIdentityMethodInput", "method");
        var command = Create(type, args);

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() => command.Execute());

        Assert.Contains("System.Identity 3.8.0 or newer", ex.Message);
        Assert.Contains("does not fall back to the REST endpoint", ex.Message);
        AssertNoRestWrite();
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task NoHandlerOfTheMethod_NamesTheNeededVersion_AndCallsNoRestWrite(string name)
    {
        var (type, args, _, _) = Cases[name];
        MethodFakes.AnswerMethodCalls(_tenant, _requests, "METHOD_HANDLER_NOT_AVAILABLE");
        var command = Create(type, args);

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() => command.Execute());

        Assert.Contains("System.Identity 3.8.0 or newer", ex.Message);
        AssertNoRestWrite();
    }

    [Theory]
    [InlineData("UpdateIdentityProvider", true)]
    [InlineData("DeleteApiSecretClient", true)]
    [InlineData("UpdateClient", true)]
    [InlineData("DeleteGroup", false)]
    public async Task VersionMessage_NamesTheSwitchOnlyForTheSecretCarryingMethods(string name, bool namesSwitch)
    {
        var (type, args, _, _) = Cases[name];
        MethodFakes.AnswerMethodCalls(_tenant, _requests, "METHOD_HANDLER_NOT_AVAILABLE");

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() => Create(type, args).Execute());

        Assert.Equal(namesSwitch, ex.Message.Contains("Methods:Identity:EnablePasswordMethods"));
    }

    [Theory]
    [InlineData(GraphQlDtos.GraphQlConstants.GetApiScopeRtIdByName)]
    [InlineData(GraphQlDtos.GraphQlConstants.GetApiResourceRtIdByName)]
    public async Task RuntimeIdLookups_AreValidAgainstTheAssetRepositorySchema(string document)
    {
        var errors = await AssetRepositoryGraphQlSchema.ValidateAsync(document, "{\"name\":\"telemetry\"}");

        Assert.Empty(errors);
    }

    [Fact]
    public async Task UnknownApiScope_FailsBeforeAnyWrite()
    {
        A.CallTo(() => _tenant.SendQueryAsync<RtApiScopeDto>(A<GraphQLRequest>._)).Returns(
            Task.FromResult<QlItemsContainer<RtApiScopeDto>?>(new QlItemsContainer<RtApiScopeDto> { Items = [] }));
        var command = Create(typeof(DeleteApiScope), ["-n", "nope", "-y"]);

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() => command.Execute());

        Assert.Equal("API scope 'nope' not found.", ex.Message);
        Assert.Empty(_requests);
        AssertNoRestWrite();
    }

    [Fact]
    public async Task UnknownRoleId_FailsBeforeAnyWrite_WithTheRestMessage()
    {
        var command = Create(typeof(UpdateGroupRoles), ["-id", GroupRtId, "-rids", "65f0c0ffee00000000000fff"]);

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() => command.Execute());

        Assert.Equal("Role with id '65f0c0ffee00000000000fff' not found.", ex.Message);
        Assert.Empty(_requests);
        AssertNoRestWrite();
    }

    [Fact]
    public async Task UpdateGroupRoles_ARoleNamedTwice_IsSentOnce()
    {
        var command = Create(typeof(UpdateGroupRoles), ["-id", GroupRtId, "-rids", ReaderRoleRtId + "," + ReaderRoleRtId]);

        await command.Execute();

        var variables = Variables(Assert.Single(_requests));
        Assert.Contains("\"roleNames\":[\"reader\"]", variables);
    }

    [Fact]
    public async Task UpdateGroupRoles_SendsTheRoleNames()
    {
        var command = Create(typeof(UpdateGroupRoles), ["-id", GroupRtId, "-rids", ReaderRoleRtId]);

        await command.Execute();

        var variables = Variables(Assert.Single(_requests));
        Assert.Contains("\"reader\"", variables);
        Assert.DoesNotContain(ReaderRoleRtId, variables);
    }

    [Fact]
    public async Task UnknownDataPermission_FailsBeforeAnyWrite_WithTheRestMessage()
    {
        var command = Create(typeof(GrantDataPermission), ["-pid", "no-such", "-r", "reader"]);

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() => command.Execute());

        Assert.Equal("Data permission 'no-such' not found.", ex.Message);
        Assert.Empty(_requests);
    }

    [Fact]
    public async Task UnknownEnforcementMode_FailsBeforeAnyWrite_WithTheRestMessage()
    {
        var command = Create(typeof(SetDataPolicyEnforcementMode), ["-id", PolicyRtId, "-m", "Sometimes"]);

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() => command.Execute());

        Assert.Equal("EnforcementMode must be Enforce|AuditOnly.", ex.Message);
        Assert.Empty(_requests);
    }

    [Fact]
    public async Task ClientWithoutRuntimeId_NamesTheNeededVersion_AndCallsNoRestWrite()
    {
        A.CallTo(() => _identity.GetClient(A<string>._)).Returns(Task.FromResult(new ClientDto { ClientId = "ci-client" }));
        var command = Create(typeof(AddClientToRole), ["-id", "ci-client", "-r", "reader"]);

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() => command.Execute());

        Assert.Contains("System.Identity 3.8.0 or newer", ex.Message);
        Assert.Empty(_requests);
        AssertNoRestWrite();
    }

    [Fact]
    public async Task ApplyClientOverlay_LogsTheCountsOfTheMethodResult()
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, null, result: new RtClientOverlayUrisResultRecordDto
        {
            OverlayName = "dev", ClientId = "ci-client", RedirectUrisAdded = 1, RedirectUrisSkipped = 2,
            PostLogoutRedirectUrisAdded = 3, PostLogoutRedirectUrisSkipped = 4, AllowedCorsOriginsAdded = 5,
            AllowedCorsOriginsSkipped = 6
        });
        var logger = new RecordingLogger<ApplyClientOverlay>();
        var command = Create(typeof(ApplyClientOverlay), ["-id", "ci-client", "-n", "dev", "-r", "https://localhost/cb"],
            logger);

        await command.Execute();

        Assert.Equal(
            "Overlay 'dev' on 'ci-client' applied: RedirectUris=1 added / 2 skipped, PostLogoutRedirectUris=3 added / 4 skipped, AllowedCorsOrigins=5 added / 6 skipped",
            logger.Entries.Last().Message);
    }

    [Fact]
    public async Task ProvisionCurrentUser_NamesTheIdentityOfTheMethodResult()
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, null, result: new RtAdminProvisioningResultRecordDto
        {
            MappingRtId = MappingRtId, SourceTenantId = "parent", SourceUserId = UserRtId, SourceUserName = "alice",
            Outcome = "created"
        });
        var logger = new RecordingLogger<ProvisionCurrentUser>();
        var command = Create(typeof(ProvisionCurrentUser), ["-ttid", "child-a"], logger);

        await command.Execute();

        Assert.Equal(
            $"Current user provisioned in target tenant 'child-a': mapping written for user 'alice' ({UserRtId}) of tenant 'parent'",
            logger.Entries.Last().Message);
    }

    [Fact]
    public async Task CreateExternalTenantUserMapping_PrintsTheMappingAsTheRestEndpointAnswered()
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, null, result: new RtAdminProvisioningResultRecordDto
        {
            MappingRtId = MappingRtId, SourceTenantId = "parent", SourceUserId = UserRtId, SourceUserName = "alice",
            RoleIds = [ReaderRoleRtId], GroupNames = ["operators"], Outcome = "created"
        });
        var command = Create(typeof(CreateExternalTenantUserMapping),
            ["-stid", "parent", "-suid", UserRtId, "-sun", "alice"]);

        await command.Execute();

        var printed = Assert.Single(_consoleLines);
        var mapping = Newtonsoft.Json.Linq.JObject.Parse(printed);
        Assert.Equal(MappingRtId, (string?)mapping["Id"]);
        Assert.Equal("alice", (string?)mapping["SourceUserName"]);
        Assert.Equal("operators", (string?)mapping["GroupNames"]![0]);
        Assert.Equal(ReaderRoleRtId, (string?)mapping["RoleIds"]![0]);
    }

    [Fact]
    public async Task CreateDataPolicy_LogsThePolicyRuntimeIdOfTheMethodResult()
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, null, result: PolicyRtId);
        var logger = new RecordingLogger<CreateDataPolicy>();
        var command = Create(typeof(CreateDataPolicy), ["-pid", "plant-a", "-t", "Basic/Asset", "-a", "Read"], logger);

        await command.Execute();

        Assert.Contains(logger.Entries, e => e.Message.Contains(PolicyRtId, StringComparison.Ordinal));
    }

    private ServiceClientOctoCommand<IIdentityServicesClient> Create(Type type, string[] args, ILogger? logger = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(Options.Create(new OctoToolOptions { TenantId = "parent" }));
        services.AddSingleton(_identity);
        services.AddSingleton(_tenant);
        services.AddSingleton(_auth);
        services.AddSingleton(_confirmation);
        services.AddSingleton(FakeSecretConsole.Empty());
        var console = A.Fake<IConsoleService>();
        A.CallTo(() => console.WriteLine(A<string>._)).Invokes((string line) => _consoleLines.Add(line));
        services.AddSingleton(console);
        var provider = services.BuildServiceProvider();
        var command = (ServiceClientOctoCommand<IIdentityServicesClient>)(logger == null
            ? ActivatorUtilities.CreateInstance(provider, type)
            : ActivatorUtilities.CreateInstance(provider, type, logger));
        command.CommandArgumentValue.ParseLayer(args);
        return command;
    }

    private void AssertNoRestWrite() =>
        A.CallTo(_identity).Where(c => IsRestWrite(c))
            .MustNotHaveHappened();

    private static bool IsRestWrite(FakeItEasy.Core.IFakeObjectCall call) =>
        !call.Method.IsSpecialName && !call.Method.Name.StartsWith("Get", StringComparison.Ordinal);

    private static string Variables(GraphQLRequest request) =>
        System.Text.Json.JsonSerializer.Serialize(request.Variables);
}
