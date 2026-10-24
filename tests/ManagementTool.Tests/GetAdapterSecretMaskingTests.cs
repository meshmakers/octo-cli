using FakeItEasy;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace ManagementTool.Tests;

/// <summary>
/// AB#6286 — <c>GetAdapter</c> must not print credentials of the adapter / pipeline configuration
/// unless <c>--show-secrets</c> is given. All values here are fake.
/// </summary>
public sealed class GetAdapterSecretMaskingTests
{
    private const string AdapterId = "670000000000000000000001";
    private const string FakePassword = "fake-Passw0rd-for-tests";
    private const string FakeClientSecret = "fake-client-secret-0123";
    private const string FakeYamlSecret = "fake-yaml-api-key-777";
    private const string FakeToken = "fake-bearer-token-abc";

    private static AdapterConfigurationDto FakeConfiguration() => new(
        new RtEntityId($"System.Communication/MeshAdapter@{AdapterId}"),
        """{"Host":"eda.example.com","Port":993,"Password":"__PW__","Credentials":{"User":"u","Pwd":"x"}}""".Replace("__PW__", FakePassword),
        [
            new PipelineConfigurationDto(OctoObjectId.Parse(AdapterId),
                new RtEntityId($"System.Communication/Pipeline@{AdapterId}"), false,
                $"nodes:\n  - type: HttpRequest\n    url: https://api.example.com\n    apiKey: {FakeYamlSecret}\n",
                [
                    new ConfigurationDto(OctoObjectId.Parse(AdapterId),
                        new RtCkId<CkTypeId>("System.Communication/ServiceAccount"), "svc",
                        """{"clientId":"c1","clientSecret":"__CS__","accessToken":"__TK__","clientSecretIsSet":true,"tokenEndpoint":"https://id.example.com/token"}"""
                            .Replace("__CS__", FakeClientSecret).Replace("__TK__", FakeToken))
                ])
        ]);

    private static List<string> Run(params string[] args)
    {
        var client = A.Fake<ICommunicationServicesClient>();
        A.CallTo(() => client.GetAdapterConfigurationAsync(AdapterId))
            .Returns(Task.FromResult(FakeConfiguration()));
        var console = new SwitchServiceAccountsToImpersonationCommandTests.RecordingConsoleService();

        var command = new GetAdapterCommand(new RecordingLogger<GetAdapterCommand>(),
            Options.Create(new OctoToolOptions { TenantId = "acme" }), console, client,
            A.Fake<IAuthenticationService>(), A.Fake<IAssetServicesClient>());
        command.CommandArgumentValue.ParseLayer(["-id", AdapterId, ..args]);
        command.Execute().GetAwaiter().GetResult();
        return console.Lines;
    }

    [Theory]
    [InlineData]
    [InlineData("-j")]
    public void Default_MasksPasswordsSecretsKeysAndTokens_ButKeepsOtherSettings(params string[] args)
    {
        var output = string.Join('\n', Run(args));

        Assert.DoesNotContain(FakePassword, output);
        Assert.DoesNotContain(FakeClientSecret, output);
        Assert.DoesNotContain(FakeYamlSecret, output);
        Assert.DoesNotContain(FakeToken, output);
        Assert.Contains("********", output);
        Assert.Contains("eda.example.com", output);
        Assert.Contains("993", output);
        Assert.Contains("https://api.example.com", output);
        Assert.Contains("https://id.example.com/token", output); // tokenEndpoint is not a credential
        Assert.Contains("clientSecretIsSet", output); // status flags stay visible
    }

    [Fact]
    public void Default_OutputIsStillValidJson()
    {
        var output = string.Join('\n', Run("-j"));
        var json = JToken.Parse(output);
        Assert.Equal(AdapterId, ((string)json["AdapterRtEntityId"]!).Split('@')[1]);
    }

    [Theory]
    [InlineData("--show-secrets")]
    [InlineData("-ss")]
    public void ShowSecrets_PrintsTheClearTextValues(string flag)
    {
        var output = string.Join('\n', Run(flag));

        Assert.Contains(FakePassword, output);
        Assert.Contains(FakeClientSecret, output);
        Assert.Contains(FakeYamlSecret, output);
        Assert.Contains(FakeToken, output);
    }

    [Fact]
    public void SecretMasker_IsSecretName_JudgesByMemberName()
    {
        foreach (var name in new[]
                 {
                     "Password", "password", "clientSecret", "client_secret", "ApiKey", "accessToken", "refreshToken",
                     "privateKey", "Pwd", "ConnectionString", "RepositoryPassword", "Credentials"
                 })
        {
            Assert.True(SecretMasker.IsSecretName(name), name);
        }

        foreach (var name in new[]
                 {
                     "Host", "Port", "UserName", "tokenEndpoint", "TokenLifetime", "ClientSecretIsSet",
                     "ClientSecretKeyMissing", "ClientSecretSetAt", "ClientId", ""
                 })
        {
            Assert.False(SecretMasker.IsSecretName(name), name);
        }
    }

    [Fact]
    public void SecretMasker_MaskText_HandlesYamlJsonAndKeyValueLines()
    {
        var yaml = SecretMasker.MaskText("user: bob\npassword: s3cr3t-yaml\n  - secret=\"in-list\"\n");
        Assert.Contains("user: bob", yaml);
        Assert.DoesNotContain("s3cr3t-yaml", yaml);
        Assert.DoesNotContain("in-list", yaml);

        var json = SecretMasker.MaskText("""{"a":1,"nested":{"password":"p-json"}}""");
        Assert.DoesNotContain("p-json", json);
        Assert.Contains("\"a\":1", json);

        Assert.Equal("plain text", SecretMasker.MaskText("plain text"));
        Assert.Equal("", SecretMasker.MaskText(null));
    }
}
