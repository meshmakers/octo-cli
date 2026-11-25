using System.Net;

namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

/// <summary>
///     The rule for the MCP service URL of a context: the knowledge commands send the signed-in user's token there, so it
///     must be an absolute https URL; plain http is accepted only for a loopback host (a local development stack).
/// </summary>
internal static class McpServiceUrlRule
{
    /// <summary>Throws a <see cref="ToolException" /> when <paramref name="url" /> breaks the rule.</summary>
    public static void Check(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ToolException($"The MCP service URL '{url}' is not an absolute http(s) URL.");
        }

        if (uri.Scheme == Uri.UriSchemeHttp && !IsLoopback(uri))
        {
            throw new ToolException(
                $"The MCP service URL '{url}' uses http: the CLI sends your token to it, so only https is accepted " +
                "(http only for localhost).");
        }
    }

    private static bool IsLoopback(Uri uri) =>
        uri.IsLoopback ||
        string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));
}
