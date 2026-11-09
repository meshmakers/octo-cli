using System.Text.RegularExpressions;

namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

/// <summary>
///     Removes credentials from text before octo-cli writes it anywhere (console, log file, standard error) — AB#8570.
///     <para>
///         A failed server call surfaces as an exception whose message carries the response body. A service that runs
///         with the ASP.NET Core developer exception page (the local stack) answers a 500 with a plain-text page that
///         echoes the request headers, including <c>Authorization: Bearer …</c> and cookies; other services and proxies
///         may echo request bodies with client secrets. Every log line and every exception text passes through
///         <see cref="Redact" /> (see <see cref="RedactedLogOutput" />), so no command has to remember it.
///     </para>
///     <para>
///         What stays visible: status, the server's error message, method and URL when the text names them. What goes:
///         the request header section of the developer exception page, the values of credential headers
///         (<c>Authorization</c>, <c>Cookie</c>, API key headers), bearer tokens, JWTs, and the values of
///         credential-named fields in JSON or form bodies (<see cref="SecretMasker.IsSecretName" />).
///     </para>
/// </summary>
internal static partial class ErrorOutputRedactor
{
    /// <summary>Placeholder written instead of a removed value.</summary>
    public const string Placeholder = "[REDACTED]";

    /// <summary>Placeholder written instead of the request header section of a developer exception page.</summary>
    public const string HeaderSectionPlaceholder = "[request headers removed by octo-cli]";

    /// <summary>
    ///     Returns <paramref name="text" /> without credentials. Text without credentials is returned unchanged
    ///     (the same instance).
    /// </summary>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var result = DeveloperPageHeaderSection().Replace(text, m => m.Groups["head"].Value + HeaderSectionPlaceholder);
        result = CredentialHeader().Replace(result, m => m.Groups["name"].Value + m.Groups["sep"].Value + Placeholder);
        result = BearerToken().Replace(result, m => m.Groups["scheme"].Value + " " + Placeholder);
        result = Jwt().Replace(result, Placeholder);
        result = JsonMember().Replace(result, m => SecretMasker.IsSecretName(m.Groups["key"].Value)
            ? m.Groups["prefix"].Value + Placeholder + m.Groups["quote"].Value
            : m.Value);
        result = FormField().Replace(result, m => SecretMasker.IsSecretName(m.Groups["key"].Value)
            ? m.Groups["prefix"].Value + Placeholder
            : m.Value);

        return result == text ? text : result;
    }

    // The plain-text developer exception page ends with "HEADERS\n=======\n<name>: <value>\n…". The whole section is
    // dropped: request headers carry no diagnostic value for the caller and any of them may hold a credential.
    [GeneratedRegex(@"(?<head>^[ \t]*HEADERS[ \t]*\r?\n[ \t]*=+[ \t]*\r?\n)(?:[^\r\n]+(?:\r?\n|$))*",
        RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex DeveloperPageHeaderSection();

    // "Authorization: Bearer x", "Cookie: a=b", "X-Api-Key=…", also quoted JSON-style ("Authorization":"…").
    [GeneratedRegex(
        """(?<name>["']?\b(?:proxy-authorization|authorization|set-cookie|cookie|x-api-key|api-key|apikey|x-auth-token|x-access-token|x-csrf-token|x-xsrf-token|x-amz-security-token)\b["']?)(?<sep>[ \t]*[:=][ \t]*)(?<val>"[^"\r\n]*"|'[^'\r\n]*'|[^\r\n]*)""",
        RegexOptions.IgnoreCase)]
    private static partial Regex CredentialHeader();

    // A bearer token anywhere in free text, e.g. in a proxy's echo of the request line. The value is an RFC 6750
    // b64token; it is redacted when it is 8 characters or longer, or shorter but holding a digit or one of -._~+/=
    // ("Bearer abc123"). A plain short word after "bearer" ("bearer of") stays.
    [GeneratedRegex(
        @"(?<scheme>\bbearer)[ \t]+(?!\[REDACTED\])(?:[A-Za-z0-9\-._~+/]{8,}|(?=[A-Za-z]*[0-9\-._~+/=])[A-Za-z0-9\-._~+/]+)=*",
        RegexOptions.IgnoreCase)]
    private static partial Regex BearerToken();

    // A JSON web token (header.payload.signature, header and payload base64url of a JSON object).
    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{5,}\.eyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]*")]
    private static partial Regex Jwt();

    // "clientSecret": "value" in a JSON body (escaped quotes inside the value included).
    [GeneratedRegex("""(?<prefix>(?<q1>["'])(?<key>[A-Za-z0-9_.\-]+)\k<q1>[ \t]*:[ \t]*(?<quote>["']))(?:\\.|(?!\k<quote>)[^\\\r\n])*\k<quote>""")]
    private static partial Regex JsonMember();

    // client_secret=value in a form body or query string: up to the next '&', whitespace or quote.
    [GeneratedRegex(@"(?<prefix>(?:^|(?<=[?&\s;,]))(?<key>[A-Za-z0-9_.\-]+)=)(?!\[REDACTED\])[^&\s""']+")]
    private static partial Regex FormField();
}
