using System.Net;
using FakeItEasy;
using GraphQL;
using GraphQlDtos.DataTransferObjects.System.Identity.v3;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.Clients;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.Methods;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace ManagementTool.Tests;

/// <summary>
///     <c>SetClientMirrorRoles</c> and <c>GetClientMirrorRoles</c> (System.Identity 3.9.0): the roles a client hands to its
///     mirrors. <c>SetClientMirrorRoles</c> runs <c>Client.SetMirrorRoles-1</c> as a dry run unless <c>--apply</c> is given,
///     prints one line per child tenant (status, roles before, roles after), and ends non-zero for a failed mirror, a
///     refusal of the method or an unknown client; it never calls a REST write. <c>GetClientMirrorRoles</c> reads
///     <c>Client.MirrorRoleNames</c> with a GraphQL query.
/// </summary>
public class ClientMirrorRolesCommandTests
{
    private const string ClientRtId = "65f0c0ffee00000000000004";

    private readonly IIdentityServicesClient _identity = A.Fake<IIdentityServicesClient>();
    private readonly ITenantClient _tenant = A.Fake<ITenantClient>();
    private readonly IAuthenticationService _auth = A.Fake<IAuthenticationService>();
    private readonly List<string> _consoleLines = [];
    private readonly List<GraphQLRequest> _requests = [];

    public ClientMirrorRolesCommandTests()
    {
        A.CallTo(() => _identity.GetClient(A<string>._)).ReturnsLazily((string id) =>
            Task.FromResult(new ClientDto { ClientId = id, RtId = ClientRtId }));
    }

    private static RtClientMirrorRolesResultRecordDto Plan(bool dryRun, params RtClientMirrorRoleTenantResultRecordDto[] tenants) =>
        new() { DryRun = dryRun, RoleNames = ["CommunicationManagement"], Tenants = tenants.ToList() };

    private static RtClientMirrorRoleTenantResultRecordDto Tenant(string id, string outcome, string[]? added = null,
        string[]? removed = null, string[]? present = null, string[]? adopted = null, string[]? handSet = null,
        string[]? missing = null, string[]? declined = null, string[]? refused = null) =>
        new()
        {
            ChildTenantId = id, Outcome = outcome, Added = added ?? [], Removed = removed ?? [], AlreadyPresent = present ?? [],
            Adopted = adopted ?? [], HandSet = handSet ?? [], RoleMissing = missing ?? [], Declined = declined ?? [],
            RoleRefused = refused ?? []
        };

    // ------------------------------------------------------------------------------------------- SetClientMirrorRoles

    [Fact]
    public async Task WithoutApply_IsADryRun_SendsDryRunTrue_AndPrintsOneLinePerChildTenant()
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, null, result: Plan(true,
            Tenant("plant-b", "Planned", missing: ["CommunicationManagement"]),
            Tenant("plant-a", "Planned", added: ["CommunicationManagement"], handSet: ["Reader"])));

        await Create<SetClientMirrorRoles>("-id", "ci-deploy", "-r", "CommunicationManagement").Execute();

        var request = Assert.Single(_requests);
        Assert.Contains("setMirrorRoles(", request.Query, StringComparison.Ordinal);
        var variables = Variables(request);
        Assert.Contains(ClientRtId, variables, StringComparison.Ordinal);
        Assert.Contains("\"dryRun\":true", variables, StringComparison.Ordinal);
        Assert.Contains("\"roleNames\":[\"CommunicationManagement\"]", variables, StringComparison.Ordinal);
        Assert.Equal(3, _consoleLines.Count);
        Assert.StartsWith("Tenant", _consoleLines[0]);
        Assert.Equal(["plant-a", "WOULD-ADD,HAND-SET", "Reader", "CommunicationManagement,Reader"], Cells(_consoleLines[1]));
        Assert.Equal(["plant-b", "ROLE-MISSING", "-", "-"], Cells(_consoleLines[2]));
        AssertNoRestWrite();
    }

    [Fact]
    public async Task Apply_SendsDryRunFalse_AndPrintsWhatHappened()
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, null, result: Plan(false,
            Tenant("plant-a", "Applied", added: ["CommunicationManagement"], removed: ["Old"]),
            Tenant("plant-c", "InSync", present: ["CommunicationManagement"], adopted: ["CommunicationManagement"]),
            Tenant("plant-d", "InSync", declined: ["CommunicationManagement"], refused: ["Deploy"])));

        await Create<SetClientMirrorRoles>("-id", "ci-deploy", "-r", "CommunicationManagement,Deploy", "--apply").Execute();

        Assert.Contains("\"dryRun\":false", Variables(Assert.Single(_requests)), StringComparison.Ordinal);
        Assert.Equal(["plant-a", "ADDED,REMOVED", "Old", "CommunicationManagement"], Cells(_consoleLines[1]));
        Assert.Equal(["plant-c", "ADOPTED", "CommunicationManagement", "CommunicationManagement"], Cells(_consoleLines[2]));
        Assert.Equal(["plant-d", "DECLINED,ROLE-REFUSED", "-", "-"], Cells(_consoleLines[3]));
    }

    [Fact]
    public async Task AnyBuiltInRoleName_IsPassedThroughTheList_Unchanged()
    {
        // No role is hard-coded: a later built-in role (e.g. WorkloadDeployment) goes through the same list.
        MethodFakes.AnswerMethodCalls(_tenant, _requests, null, result: Plan(true));

        await Create<SetClientMirrorRoles>("-id", "ci-deploy", "-r", " WorkloadDeployment , Reader,reader").Execute();

        Assert.Contains("\"roleNames\":[\"WorkloadDeployment\",\"Reader\"]", Variables(Assert.Single(_requests)),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clear_SendsAnEmptyList()
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, null, result: Plan(true));

        await Create<SetClientMirrorRoles>("-id", "ci-deploy", "--clear").Execute();

        Assert.Contains("\"roleNames\":[]", Variables(Assert.Single(_requests)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData]
    [InlineData("-r", "Reader", "--clear")]
    [InlineData("-r", " , ")]
    public async Task NeitherOrBothOfRolesAndClear_EndsBeforeAnyCall(params string[] args)
    {
        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            Create<SetClientMirrorRoles>(["-id", "ci-deploy", .. args]).Execute());

        Assert.Contains("--", ex.Message);
        Assert.Empty(_requests);
    }

    [Fact]
    public async Task AFailedMirror_PrintsTheTable_AndEndsNonZero_NamingTheTenant()
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, null, result: Plan(false,
            Tenant("plant-a", "Applied", added: ["CommunicationManagement"]),
            Tenant("plant-x", "Failed", present: ["Reader"])));

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() =>
            Create<SetClientMirrorRoles>("-id", "ci-deploy", "-r", "CommunicationManagement", "--apply").Execute());

        Assert.Contains("plant-x", ex.Message);
        Assert.DoesNotContain("plant-a", ex.Message);
        Assert.Equal(["plant-x", "FAILED,ALREADY-PRESENT", "Reader", "Reader"], Cells(_consoleLines[2]));
    }

    [Theory]
    [InlineData("MIRROR_ROLE_NOT_ALLOWED", "TenantManagement")]
    [InlineData("MIRROR_ROLE_NOT_FOUND", "Unknown")]
    [InlineData("CLIENT_NOT_CONFIDENTIAL", null)]
    [InlineData("CONCURRENT_CHANGE", null)]
    public async Task ADeclaredError_EndsNonZero_WithCodeAndRefusedNames(string code, string? roleNames)
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, code,
            parameters: roleNames == null ? null : new Dictionary<string, object?> { ["roleNames"] = roleNames });

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() =>
            Create<SetClientMirrorRoles>("-id", "ci-deploy", "-r", roleNames ?? "Reader").Execute());

        Assert.Contains(code, ex.Message);
        if (roleNames != null)
        {
            Assert.Contains(roleNames, ex.Message);
        }

        Assert.Empty(_consoleLines);
        AssertNoRestWrite();
    }

    [Fact]
    public async Task ACallerWithoutTenantManagement_IsRefused_WithTheReason()
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, "METHOD_FORBIDDEN",
            parameters: new Dictionary<string, object?> { ["reason"] = "role-required" });

        var ex = await Assert.ThrowsAsync<CkMethodCallException>(() =>
            Create<SetClientMirrorRoles>("-id", "ci-deploy", "-r", "Reader").Execute());

        Assert.Contains("METHOD_FORBIDDEN", CkMethodErrorText.Format(ex));
        Assert.Contains("reason=role-required", CkMethodErrorText.Format(ex));
        Assert.Empty(_consoleLines);
    }

    [Fact]
    public async Task AnUnknownClient_EndsBeforeTheMethodCall_NamingClientAndTenant()
    {
        A.CallTo(() => _identity.GetClient("missing")).ThrowsAsync(
            new ServiceClientResultException("Client not found", HttpStatusCode.NotFound, null));

        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            Create<SetClientMirrorRoles>("-id", "missing", "-r", "Reader").Execute());

        Assert.Contains("'missing'", ex.Message);
        Assert.Contains("'parent'", ex.Message);
        Assert.Empty(_requests);
    }

    [Fact]
    public async Task AServerWithoutTheMethod_NamesSystemIdentity390()
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, "METHOD_HANDLER_NOT_AVAILABLE");

        var ex = await Assert.ThrowsAsync<ServiceClientException>(() =>
            Create<SetClientMirrorRoles>("-id", "ci-deploy", "-r", "Reader").Execute());

        Assert.Contains("System.Identity 3.9.0", ex.Message);
        Assert.Contains("Nothing was changed", ex.Message);
    }

    [Fact]
    public async Task Json_PrintsTheMethodResult()
    {
        MethodFakes.AnswerMethodCalls(_tenant, _requests, null, result: Plan(true,
            Tenant("plant-a", "Planned", added: ["CommunicationManagement"])));

        await Create<SetClientMirrorRoles>("-id", "ci-deploy", "-r", "CommunicationManagement", "-j").Execute();

        var json = JObject.Parse(Assert.Single(_consoleLines));
        Assert.True((bool)json["DryRun"]!);
        Assert.Equal("plant-a", (string?)json["Tenants"]![0]!["ChildTenantId"]);
    }

    // ------------------------------------------------------------------------------------------- GetClientMirrorRoles

    [Fact]
    public async Task GetClientMirrorRoles_PrintsTheListOfTheOriginal_FromAGraphQlQuery()
    {
        AnswerClientQuery(new RtClientDto { ClientId = "ci-deploy", MirrorRoleNames = ["Reader", "CommunicationManagement"] });

        await Create<GetClientMirrorRoles>("-id", "ci-deploy").Execute();

        Assert.Equal(["Client | Mirror roles", "ci-deploy | CommunicationManagement,Reader"], _consoleLines);
        A.CallTo(() => _auth.EnsureAuthenticated(_tenant.AccessToken)).MustHaveHappened();
        AssertNoRestWrite();
    }

    [Fact]
    public async Task GetClientMirrorRoles_Json_AndAnEmptyList()
    {
        AnswerClientQuery(new RtClientDto { ClientId = "ci-deploy" });

        await Create<GetClientMirrorRoles>("-id", "ci-deploy", "-j").Execute();

        var json = JObject.Parse(Assert.Single(_consoleLines));
        Assert.Equal("ci-deploy", (string?)json["clientId"]);
        Assert.Empty((JArray)json["mirrorRoleNames"]!);
    }

    [Fact]
    public async Task GetClientMirrorRoles_UnknownClient_EndsNonZero_NamingClientAndTenant()
    {
        AnswerClientQuery();

        var ex = await Assert.ThrowsAsync<ToolException>(() => Create<GetClientMirrorRoles>("-id", "missing").Execute());

        Assert.Contains("'missing'", ex.Message);
        Assert.Contains("'parent'", ex.Message);
        Assert.Empty(_consoleLines);
    }

    // ------------------------------------------------------------------------------------------------------ helpers

    private void AnswerClientQuery(params RtClientDto[] clients) =>
        A.CallTo(() => _tenant.SendQueryAsync<RtClientDto>(A<GraphQLRequest>._)).ReturnsLazily((GraphQLRequest r) =>
        {
            _requests.Add(r);
            return Task.FromResult<QlItemsContainer<RtClientDto>?>(new QlItemsContainer<RtClientDto> { Items = clients });
        });

    private static string[] Cells(string line) => line.Split('|').Select(c => c.Trim()).ToArray();

    private T Create<T>(params string[] args) where T : ServiceClientOctoCommand<IIdentityServicesClient>
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(Options.Create(new OctoToolOptions { TenantId = "parent" }));
        services.AddSingleton(_identity);
        services.AddSingleton(_tenant);
        services.AddSingleton(_auth);
        var console = A.Fake<IConsoleService>();
        A.CallTo(() => console.WriteLine(A<string>._)).Invokes((string line) => _consoleLines.Add(line));
        services.AddSingleton(console);
        var command = ActivatorUtilities.CreateInstance<T>(services.BuildServiceProvider());
        command.CommandArgumentValue.ParseLayer(args);
        return command;
    }

    private void AssertNoRestWrite() =>
        A.CallTo(_identity).Where(c => !c.Method.IsSpecialName && !c.Method.Name.StartsWith("Get", StringComparison.Ordinal))
            .MustNotHaveHappened();

    private static string Variables(GraphQLRequest request) => System.Text.Json.JsonSerializer.Serialize(request.Variables);
}
