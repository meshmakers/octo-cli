using System;

namespace Meshmakers.Octo.Frontend.ManagementTool;

public class OctoToolAuthenticationOptions
{
    public string? AccessToken { get; set; }

    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public DateTime? AccessTokenExpiresAt { get; set; }
    public string? RefreshToken { get; set; }
}
