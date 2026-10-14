using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FakeItEasy;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.General.Authentication;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using UserInfoData = Meshmakers.Octo.Sdk.ServiceClient.Authorization.UserInfoData;

namespace ManagementTool.Tests;

/// <summary>
/// AB#5744 — <c>AuthStatus</c> must not print the raw access token, its signature or the refresh token
/// unless <c>--show-token</c> is given; claims and expiry stay visible. Tokens here are fake and signed
/// with a throw-away key.
/// </summary>
public sealed class AuthStatusCommandTests
{
    private const string RefreshToken = "fake-refresh-token-0123456789";

    private static string FakeJwt()
    {
        var key = new SymmetricSecurityKey(new byte[32].Select((_, i) => (byte)(i + 1)).ToArray());
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(new JwtSecurityToken("https://identity.example.com", "octo-api",
            [new Claim("sub", "user-1"), new Claim("email", "a@example.com"), new Claim("tenant_id", "acme")],
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddHours(1),
            new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));
    }

    private static (List<string> Lines, string AccessToken, string Signature) Run(params string[] args)
    {
        var accessToken = FakeJwt();
        var signature = accessToken.Split('.')[2];

        var client = A.Fake<IAuthenticatorClient>();
        A.CallTo(() => client.GetUserInfoAsync(A<string>._))
            .Returns(Task.FromResult(new UserInfoData(true, [new Claim("sub", "user-1")])));
        var contextManager = A.Fake<IContextManager>();
        var console = new SwitchServiceAccountsToImpersonationCommandTests.RecordingConsoleService();

        var command = new AuthStatusCommand(new RecordingLogger<AuthStatusCommand>(), console,
            Options.Create(new OctoToolOptions { IdentityServiceUrl = "https://identity.example.com/" }),
            Options.Create(new OctoToolAuthenticationOptions
            {
                AccessToken = accessToken, RefreshToken = RefreshToken
            }), client, A.Fake<IAuthenticationService>(), contextManager);

        command.CommandArgumentValue.ParseLayer(args);
        command.Execute().GetAwaiter().GetResult();
        return (console.Lines, accessToken, signature);
    }

    [Fact]
    public void Default_MasksAccessTokenSignatureAndRefreshToken_ButKeepsClaimsAndExpiry()
    {
        var (lines, accessToken, signature) = Run();
        var output = string.Join('\n', lines);

        Assert.DoesNotContain(accessToken, output);
        Assert.DoesNotContain(signature, output);
        Assert.DoesNotContain(RefreshToken, output);
        Assert.DoesNotContain("eyJ", output);
        Assert.Contains("masked", output);
        Assert.Contains(lines, l => l.StartsWith("Valid To "));
        Assert.Contains(lines, l => l.Contains("email") && l.Contains("a@example.com"));
        Assert.Contains(lines, l => l.Contains("tenant_id") && l.Contains("acme"));
        Assert.Contains(lines, l => l.StartsWith("Issuer ") && l.Contains("https://identity.example.com"));
    }

    [Theory]
    [InlineData("--show-token")]
    [InlineData("-st")]
    public void ShowToken_PrintsAccessTokenSignatureAndRefreshToken(string flag)
    {
        var (lines, accessToken, signature) = Run(flag);

        Assert.Contains(accessToken, lines);
        Assert.Contains(lines, l => l.StartsWith("Signature ") && l.Contains(signature));
        Assert.Contains(RefreshToken, lines);
    }

    [Fact]
    public void SecretMasker_RevealsOnlyTheLength()
    {
        Assert.Equal("(not set)", SecretMasker.Mask(null));
        Assert.Equal("(not set)", SecretMasker.Mask(""));
        var masked = SecretMasker.Mask("hunter2-secret");
        Assert.DoesNotContain("hunter2", masked);
        Assert.Contains("14", masked);
    }
}
