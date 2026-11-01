using System.Net;
using System.Text;
using FakeItEasy;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.AdminProvisioning;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
/// AB#8017: ProvisionCurrentUser names the identity the mapping was written for. From a context scoped to an
/// intermediate tenant the identity service provisions the home identity of the shadow user, so the operator must see
/// which source tenant and user now reach the target. The real <see cref="IdentityServicesClient" /> talks to a local
/// HTTP listener that answers like the identity service.
/// </summary>
public sealed class ProvisionCurrentUserOutputTests : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly string _baseUri;
    private string? _requestPath;
    private string _responseJson = "{}";

    public ProvisionCurrentUserOutputTests()
    {
        var started = false;
        var uri = "";
        for (var port = 47000 + Random.Shared.Next(0, 2000); !started; port++)
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
            var bytes = Encoding.UTF8.GetBytes(_responseJson);
            context.Response.StatusCode = (int)HttpStatusCode.Created;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }

    private (ProvisionCurrentUser Command, RecordingLogger<ProvisionCurrentUser> Logger) CreateCommand()
    {
        var client = new IdentityServicesClient(
            new IdentityServiceClientOptions { EndpointUri = _baseUri, TenantId = "tmp-a" },
            A.Fake<IIdentityServiceClientAccessToken>());
        var logger = new RecordingLogger<ProvisionCurrentUser>();
        var command = new ProvisionCurrentUser(logger, Options.Create(new OctoToolOptions { TenantId = "tmp-a" }),
            client, A.Fake<IAuthenticationService>());
        command.CommandArgumentValue.ParseLayer(["-ttid", "tmp-b"]);
        return (command, logger);
    }

    [Fact]
    public async Task Provision_NamesTheSourceTenantAndUserTheMappingWasWrittenFor()
    {
        _responseJson = "{\"sourceTenantId\":\"octosystem\",\"sourceUserId\":\"686a6215fd3e5972ff5693fd\"," +
                        "\"sourceUserName\":\"alice@example.com\",\"roleIds\":[],\"groupNames\":[\"TenantOwners\"]}";
        var (command, logger) = CreateCommand();

        await command.Execute();

        Assert.EndsWith("/adminProvisioning/tmp-b/provisionCurrentUser", _requestPath);
        var line = Assert.Single(logger.Entries,
            e => e.Level == LogLevel.Information && e.Message.StartsWith("Current user provisioned"));
        Assert.Contains("'tmp-b'", line.Message);
        Assert.Contains("'alice@example.com'", line.Message);
        Assert.Contains("686a6215fd3e5972ff5693fd", line.Message);
        Assert.Contains("tenant 'octosystem'", line.Message);
    }

    [Fact]
    public async Task Provision_WithoutResponseBody_StillReportsSuccess()
    {
        _responseJson = "";
        var (command, logger) = CreateCommand();

        await command.Execute();

        Assert.Contains(logger.Entries, e => e.Message == "Current user provisioned in target tenant 'tmp-b'");
    }
}
