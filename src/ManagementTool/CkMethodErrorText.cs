using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.Methods;

namespace Meshmakers.Octo.Frontend.ManagementTool;

/// <summary>
///     AB#8417: one line for a refused CK method call — the error code first, then the message and the parameters the
///     service sent (e.g. <c>reason=self-grant</c> of a <c>METHOD_FORBIDDEN</c>). The parameter <c>methodId</c> is left
///     out; the message of a <see cref="CkMethodCallException" /> already names the method.
/// </summary>
internal static class CkMethodErrorText
{
    /// <summary><c>CODE: message (name=value, …)</c>; <paramref name="message" /> replaces the message of the service.</summary>
    public static string Format(CkMethodError error, string? message = null) =>
        $"{error.Code}: {(string.IsNullOrWhiteSpace(message) ? error.Message : message)}{ParameterSuffix(error)}";

    /// <summary>
    ///     The exception's message (it names the method and the code) and the parameters, e.g.
    ///     <c>The CK method call 'System.Identity/User.AddRole-1' failed: METHOD_FORBIDDEN (reason=self-grant)</c>.
    /// </summary>
    public static string Format(CkMethodCallException exception) => exception.Message + ParameterSuffix(exception.Error);

    private static string ParameterSuffix(CkMethodError error)
    {
        var parameters = error.Parameters
            .Where(p => !string.Equals(p.Key, "methodId", StringComparison.Ordinal))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={p.Value}")
            .ToList();
        return parameters.Count == 0 ? string.Empty : $" ({string.Join(", ", parameters)})";
    }
}
