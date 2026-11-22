using System.Net;
using System.Text;
using FakeItEasy;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Tenants;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     <c>Create --blueprint</c> creates the tenant with a catalog blueprint applied in one call; the asset
///     repository applies it in the create transaction and answers 400 when it fails (no tenant left behind).
/// </summary>
public sealed class CreateTenantBlueprintCommandTests : IDisposable
{
    private const string FailureMessage = "Blueprint application failed: Blueprint 'Unknown-9.9.9' not found";

    private readonly HttpListener _listener = new();
    private readonly string _baseUri;
    private readonly List<string> _requests = [];
    private int _statusCode = 204;
    private string? _responseBody;

    public CreateTenantBlueprintCommandTests()
    {
        var started = false;
        var uri = "";
        var firstPort = 45000 + Random.Shared.Next(0, 2000);
        for (var port = firstPort; !started; port++)
        {
            try
            {
                uri = $"http://127.0.0.1:{port}/";
                _listener.Prefixes.Clear();
                _listener.Prefixes.Add(uri);
                _listener.Start();
                started = true;
            }
            catch (HttpListenerException) when (port < firstPort + 100)
            {
                // port taken, try the next one (bounded: any other startup failure surfaces after 100 attempts)
            }
        }

        _baseUri = uri;
        _ = Task.Run(Serve);
    }

    public void Dispose() => _listener.Close();

    private async Task Serve()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception)
            {
                return;
            }

            lock (_requests)
            {
                _requests.Add($"{context.Request.HttpMethod} {context.Request.Url?.PathAndQuery}");
            }

            context.Response.StatusCode = _statusCode;
            if (_responseBody != null)
            {
                var bytes = Encoding.UTF8.GetBytes(_responseBody);
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
            }

            context.Response.Close();
        }
    }

    private static (CreateTenant Command, IAssetServicesClient Asset, IIdentityServicesClient Identity) NewCreate()
    {
        var asset = A.Fake<IAssetServicesClient>();
        var identity = A.Fake<IIdentityServicesClient>();
        A.CallTo(() => asset.AccessToken).Returns(A.Fake<IServiceClientAccessToken>());
        var command = new CreateTenant(NullLogger<CreateTenant>.Instance,
            Options.Create(new OctoToolOptions { TenantId = "parent1" }), asset, identity,
            A.Fake<IAuthenticationService>());
        return (command, asset, identity);
    }

    [Fact]
    public async Task Create_WithBlueprint_SendsTheBlueprint_AndProvisionsTheUserAfterwards()
    {
        var (command, asset, identity) = NewCreate();
        command.CommandArgumentValue.ParseLayer(["-tid", "Starter1", "--blueprint", "Octo.Starter-1.2.0"]);

        await command.Execute();

        A.CallTo(() => asset.CreateTenantAsync("starter1", null, false, "Octo.Starter-1.2.0"))
            .MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => identity.ProvisionCurrentUser("starter1")).MustHaveHappenedOnceExactly());
        A.CallTo(() => asset.CreateTenantAsync(A<string>._, A<string?>._)).MustNotHaveHappened();
        A.CallTo(() => asset.CreateTenantAsync(A<string>._, A<string?>._, A<bool>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Create_WithBlueprintScratchAndDatabase_CombinesThem_AndProvisionsNoUser()
    {
        var (command, asset, identity) = NewCreate();
        command.CommandArgumentValue.ParseLayer(["-tid", "Scratch1", "-db", "Scratch1_Db", "-sc", "-b", "Octo.Starter-1.2.0"]);

        await command.Execute();

        A.CallTo(() => asset.CreateTenantAsync("scratch1", "scratch1_db", true, "Octo.Starter-1.2.0"))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => identity.ProvisionCurrentUser(A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Create_WithoutBlueprint_DoesNotUseTheBlueprintOverload()
    {
        var (command, asset, _) = NewCreate();
        command.CommandArgumentValue.ParseLayer(["-tid", "plain1", "-db", "plain1_db"]);

        await command.Execute();

        A.CallTo(() => asset.CreateTenantAsync("plain1", "plain1_db")).MustHaveHappenedOnceExactly();
        A.CallTo(() => asset.CreateTenantAsync(A<string>._, A<string?>._, A<bool>._, A<string?>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Create_WithFailingBlueprint_ProvisionsNoUser()
    {
        var (command, asset, identity) = NewCreate();
        A.CallTo(() => asset.CreateTenantAsync("t1", null, false, "Unknown-9.9.9"))
            .ThrowsAsync(new ServiceClientResultException(FailureMessage, HttpStatusCode.BadRequest, null));
        command.CommandArgumentValue.ParseLayer(["-tid", "t1", "-b", "Unknown-9.9.9"]);

        await Assert.ThrowsAsync<ServiceClientResultException>(command.Execute);

        A.CallTo(() => identity.ProvisionCurrentUser(A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Create_WithFailingBlueprint_ExitsNonZero_WithTheServiceMessage()
    {
        // The real SDK client against a local listener that answers as the asset repository does when the blueprint
        // fails (TenantsController: transaction aborted, 400 OperationFailedErrorDto).
        _statusCode = 400;
        _responseBody = "{\"statusCode\":400,\"statusDescription\":\"Bad Request\",\"message\":\"" + FailureMessage + "\"}";
        var asset = new AssetServicesClient(
            new AssetServiceClientOptions { EndpointUri = _baseUri, TenantId = "parent1" },
            A.Fake<IAssetServiceClientAccessToken>());
        var identity = A.Fake<IIdentityServicesClient>();
        var command = new CreateTenant(NullLogger<CreateTenant>.Instance,
            Options.Create(new OctoToolOptions { TenantId = "parent1" }), asset, identity,
            A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(["-tid", "t1", "-b", "Unknown-9.9.9"]);

        var parser = A.Fake<ICommandParser>();
        A.CallTo(() => parser.ParseAndValidateAsync(A<string?>._))
            .Returns(Task.Run(command.Execute, TestContext.Current.CancellationToken));
        var contexts = A.Fake<IContextManager>();
        A.CallTo(() => contexts.GetEffectiveContextName()).Returns("test");
        var logger = new RecordingLogger<Runner>();

        var exitCode = await new Runner(logger, parser, contexts, ContextSelection.None).DoActionAsync();

        Assert.NotEqual(0, exitCode);
        var error = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains(FailureMessage, error.Message);
        Assert.Single(_requests);
        Assert.Contains("blueprintId=Unknown-9.9.9", _requests[0]);
        A.CallTo(identity).Where(call => call.Method.Name == nameof(IIdentityServicesClient.ProvisionCurrentUser))
            .MustNotHaveHappened();
    }

    [Fact]
    public void Create_Help_NamesTheTransactionalBehaviour()
    {
        var (command, _, _) = NewCreate();

        var notes = command.GetDocumentation()?.Notes;

        Assert.NotNull(notes);
        var note = Assert.Single(notes, n => n.Contains("--blueprint"));
        Assert.Contains("one transaction", note);
        Assert.Contains("no tenant is left behind", note);
    }
}
