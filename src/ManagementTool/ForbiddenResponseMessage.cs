using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Meshmakers.Octo.Sdk.ServiceClient;

namespace Meshmakers.Octo.Frontend.ManagementTool;

/// <summary>
///     Reads the <c>message</c> of a <c>403 {"message": "..."}</c> body of the identity API (AB#8094, server side AB#5861), e.g.
///     "...requires the role 'SecretManagement'...". The SDK keeps the response body in the text of the
///     <see cref="ServiceClientResultException" /> (<c>Forbidden: {"message":"..."}</c>); it is read here once.
/// </summary>
internal static class ForbiddenResponseMessage
{
    /// <summary>
    ///     Returns the <c>message</c> of the JSON body, or <c>false</c> when the answer had no body or no <c>message</c>
    ///     (cross-tenant refusals have an empty body).
    /// </summary>
    public static bool TryGet(ServiceClientResultException exception, [NotNullWhen(true)] out string? message)
    {
        message = null;
        var text = exception.Message;
        var start = text.IndexOf('{');
        if (start < 0)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(text.AsMemory(start));
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("message", out var element) &&
                element.ValueKind == JsonValueKind.String &&
                element.GetString() is { } reason &&
                !string.IsNullOrWhiteSpace(reason))
            {
                message = reason;
                return true;
            }
        }
        catch (JsonException)
        {
            // not a JSON body: fall back to the generic text
        }

        return false;
    }
}
