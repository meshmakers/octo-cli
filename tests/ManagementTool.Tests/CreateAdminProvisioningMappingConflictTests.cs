using System.Net;
using System.Text;
using FakeItEasy;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.AdminProvisioning;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
/// A mapping is unique per (source tenant, source user). The identity service answers a second create with
/// <c>409 application/problem+json</c> (code <c>ExternalTenantUserMappingAlreadyExists</c>, <c>existingMappingId</c>).
/// These tests pin what the operator sees and gets as exit code: the real <see cref="IdentityServicesClient" /> talks to
/// a local HTTP listener that answers the identity service's problem document, and the real <see cref="Runner" /> turns
/// the exception into the message and the exit code. The command has no catch of its own: the problem text arrives as it is.
/// </summary>
public sealed class CreateAdminProvisioningMappingConflictTests : IDisposable
{
    private const string ExistingId = "6ad4f62c1f0e4a3b9c8d7e6f";

    // The body the identity service sends (ExternalTenantUserMappingProblems.AlreadyExists), as JSON.
    private static readonly string ProblemJson =
        "{\"title\":\"A mapping for this source user already exists\",\"status\":409," +
        $"\"detail\":\"Mapping '{ExistingId}' already maps user 'u-1' of source tenant 'octosystem'. " +
        "Update its roles (PUT ExternalTenantUserMappings/{id}) instead of creating a second mapping.\"," +
        $"\"code\":\"ExternalTenantUserMappingAlreadyExists\",\"existingMappingId\":\"{ExistingId}\"}}";

    private readonly HttpListener _listener = new();
    private readonly string _baseUri;
    private string? _requestPath;
    private string? _requestBody;

    public CreateAdminProvisioningMappingConflictTests()
    {
        // HttpListener needs a free port; probe a few, the prefix must be known before Start.
        var started = false;
        var uri = "";
        for (var port = 41000 + Random.Shared.Next(0, 2000); !started; port++)
        {
            try
            {
                uri = $"http://127.0.0.1:{port}/";
                _listener.Prefixes.Clear();
                _listener.Prefixes.Add(uri);
                _listener.Start();
                started = true;
            }
            catch (HttpListenerException)
            {
                // port taken, try the next one
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

            _requestPath = context.Request.Url?.AbsolutePath;
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                _requestBody = await reader.ReadToEndAsync();
            }

            var bytes = Encoding.UTF8.GetBytes(ProblemJson);
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            context.Response.ContentType = "application/problem+json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }

    private CreateAdminProvisioningMapping CreateCommand()
    {
        var client = new IdentityServicesClient(
            new IdentityServiceClientOptions { EndpointUri = _baseUri, TenantId = "octosystem" },
            A.Fake<IIdentityServiceClientAccessToken>());
        var command = new CreateAdminProvisioningMapping(
            new RecordingLogger<CreateAdminProvisioningMapping>(),
            Options.Create(new OctoToolOptions { TenantId = "octosystem" }),
            client, A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(
            ["-ttid", "customer-project", "-stid", "octosystem", "-suid", "u-1", "-sun", "alice", "-rids", "Development"]);
        return command;
    }

    [Fact]
    public async Task DuplicateCreate_FailsWithTheConflictAndTheProblemText_NotSwallowedByTheCommand()
    {
        var command = CreateCommand();

        var ex = await Assert.ThrowsAsync<ServiceClientResultException>(() => command.Execute());

        Assert.Equal(HttpStatusCode.Conflict, ex.HttpStatusCode);
        Assert.Contains("ExternalTenantUserMappingAlreadyExists", ex.Message);
        Assert.Contains(ExistingId, ex.Message);
        Assert.Contains("Update its roles", ex.Message);
        Assert.EndsWith("/adminProvisioning/customer-project", _requestPath);
        Assert.Contains("\"sourceUserId\":\"u-1\"", _requestBody!.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DuplicateCreate_RunnerPrintsTheProblemText_AndExitsWithMinusThree()
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
        Assert.Contains("ExternalTenantUserMappingAlreadyExists", error.Message);
        Assert.Contains(ExistingId, error.Message);
        Assert.Contains("Update its roles", error.Message);
    }
}
