using System.Net;
using System.Text;
using System.Text.Json;
using FakeItEasy;
using GraphQL;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.ApiSecrets;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#8008 — <c>DeleteApiSecret*</c> and <c>UpdateApiSecret*</c> send the stored
///     secret value in the JSON body, never in the request URL (request logs, traces, proxy access logs). The real
///     command and the real <see cref="IdentityServicesClient" /> talk to a local HTTP listener that records every
///     request; the value appears in no URL and no log line. <c>ResetPassword</c> moved to the CK method
///     <c>User.ResetPassword-1</c> (AB#8418); its body/URL/output check is in <see cref="UserPasswordCanaryTests" />.
/// </summary>
public sealed class CredentialsInRequestBodyTests : IDisposable
{
    // Throwaway values, generated per test run.
    private readonly string _storedHash = Convert.ToBase64String(Guid.NewGuid().ToByteArray());

    private readonly HttpListener _listener;
    private readonly string _baseUri;
    private readonly List<(string Method, string PathAndQuery, string Body)> _requests = [];
    private readonly IOptions<OctoToolOptions> _options = Options.Create(new OctoToolOptions { TenantId = "acme" });
    private readonly IConfirmationService _confirm = A.Fake<IConfirmationService>();
    private readonly List<GraphQLRequest> _methodCalls = [];
    private readonly ITenantClient _tenant;

    public CredentialsInRequestBodyTests()
    {
        // A failed Start() disposes the listener, so every attempt gets a fresh one; attempts are bounded and the last
        // failure is rethrown instead of looping on a closed listener.
        (_listener, _baseUri) = StartListener();
        A.CallTo(() => _confirm.Confirm(A<string>._)).Returns(true);
        _tenant = MethodFakes.IdentityTenant(_methodCalls);
        _ = Task.Run(Serve);
    }

    public void Dispose() => _listener.Close();

    private static (HttpListener Listener, string BaseUri) StartListener()
    {
        const int attempts = 20;
        for (var attempt = 1; ; attempt++)
        {
            var uri = $"http://127.0.0.1:{47000 + Random.Shared.Next(0, 2000)}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(uri);
            try
            {
                listener.Start();
                return (listener, uri);
            }
            catch (HttpListenerException) when (attempt < attempts)
            {
                // port taken: try another one with a new listener
                listener.Close();
            }
        }
    }

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

            string body;
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync();
            }

            lock (_requests)
            {
                _requests.Add((context.Request.HttpMethod, context.Request.Url?.PathAndQuery ?? string.Empty, body));
            }

            // A lookup answers with the secret, a client read with the client's runtime id; every other call with an
            // empty object.
            var json = context.Request.Url?.AbsolutePath.EndsWith("/lookup", StringComparison.Ordinal) == true
                ? JsonSerializer.Serialize(new { valueEncrypted = _storedHash, description = "old" })
                : context.Request.HttpMethod == "GET"
                    ? JsonSerializer.Serialize(new { clientId = "c1", rtId = MethodFakes.TargetRtId })
                    : "{}";
            var bytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }

    private IdentityServicesClient CreateClient() =>
        new(new IdentityServiceClientOptions { EndpointUri = _baseUri, TenantId = "acme" },
            A.Fake<IIdentityServiceClientAccessToken>());

    [Theory]
    [InlineData("client")]
    [InlineData("apiResource")]
    public async Task DeleteApiSecret_SendsTheStoredValueInTheBody_NotInTheUrl(string kind)
    {
        if (kind == "client")
        {
            var command = new DeleteApiSecretClient(new RecordingLogger<DeleteApiSecretClient>(), _options, CreateClient(),
                _tenant, A.Fake<IAuthenticationService>(), _confirm);
            command.CommandArgumentValue.ParseLayer(["-cid", "c1", "-s", _storedHash, "-y"]);
            await command.Execute();
        }
        else
        {
            var command = new DeleteApiSecretApiResource(new RecordingLogger<DeleteApiSecretApiResource>(), _options,
                CreateClient(), _tenant, A.Fake<IAuthenticationService>(), _confirm);
            command.CommandArgumentValue.ParseLayer(["-n", "c1", "-s", _storedHash, "-y"]);
            await command.Execute();
        }

        // The identity service sees no secret value (at most the client read); the method call carries it in its variables.
        Assert.All(_requests, r => Assert.Equal("GET", r.Method));
        Assert.DoesNotContain(_requests, r => r.PathAndQuery.Contains(_storedHash) || r.Body.Contains(_storedHash));
        AssertValueOnlyInTheMethodVariables("removeSecret");
    }

    [Theory]
    [InlineData("client")]
    [InlineData("apiResource")]
    public async Task UpdateApiSecret_LooksTheSecretUpByBody_AndUpdatesByBody(string kind)
    {
        if (kind == "client")
        {
            var command = new UpdateApiSecretClient(new RecordingLogger<UpdateApiSecretClient>(), _options, CreateClient(),
                _tenant, A.Fake<IAuthenticationService>());
            command.CommandArgumentValue.ParseLayer(["-cid", "c1", "-s", _storedHash, "-d", "new"]);
            await command.Execute();
        }
        else
        {
            var command = new UpdateApiSecretApiResource(new RecordingLogger<UpdateApiSecretApiResource>(), _options,
                CreateClient(), _tenant, A.Fake<IAuthenticationService>());
            command.CommandArgumentValue.ParseLayer(["-n", "c1", "-s", _storedHash, "-d", "new"]);
            await command.Execute();
        }

        var lookup = Assert.Single(_requests, r => r.Method == "POST");
        Assert.Equal($"/acme/v1/apiSecrets/{kind}/c1/lookup", lookup.PathAndQuery);
        AssertValueOnlyInBody(lookup);
        Assert.DoesNotContain(_requests, r => r.Method == "PUT");
        AssertValueOnlyInTheMethodVariables("updateSecret");
        Assert.Contains("new", MethodFakes.VariablesOf(_methodCalls[0]));
    }

    private void AssertValueOnlyInTheMethodVariables(string field)
    {
        var call = Assert.Single(_methodCalls);
        Assert.Contains(field, call.Query, StringComparison.Ordinal);
        Assert.DoesNotContain(_storedHash, call.Query, StringComparison.Ordinal);
        Assert.Equal(_storedHash,
            JsonSerializer.SerializeToElement(call.Variables).GetProperty("input").GetProperty("secretValue").GetString());
    }

    private void AssertValueOnlyInBody((string Method, string PathAndQuery, string Body) request)
    {
        Assert.DoesNotContain(_storedHash, Uri.UnescapeDataString(request.PathAndQuery));
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal(_storedHash, body.RootElement.GetProperty("valueEncrypted").GetString());
    }
}
