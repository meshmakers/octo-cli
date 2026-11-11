using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.Methods;

namespace Meshmakers.Octo.Frontend.ManagementTool;

/// <summary>
///     When a command that runs a CK method falls back to the REST or job API of earlier service versions (AB#8446 for
///     <c>Dump</c>, AB#8838 for the user role commands). Only when the call was certainly not executed: the asset
///     repository rejected the document before execution (GraphQL validation - every error carries the spec section
///     <c>extensions.number</c>, e.g. <c>VARIABLES_ARE_INPUT_TYPES</c>, <c>KNOWN_TYPE_NAMES</c>,
///     <c>FIELDS_ON_CORRECT_TYPE</c> from an asset repository before 3.5.43 or a tenant whose CK model lacks the method),
///     or no handler of the method is live (<c>METHOD_HANDLER_NOT_AVAILABLE</c>, e.g. a service without the method).
/// </summary>
internal static class CkMethodFallback
{
    private const string HandlerNotAvailable = "METHOD_HANDLER_NOT_AVAILABLE";

    public static bool IsMethodUnavailable(CkMethodCallException exception) =>
        string.Equals(exception.Code, HandlerNotAvailable, StringComparison.Ordinal) || IsValidationError(exception);

    public static string Describe(CkMethodCallException exception) =>
        IsValidationError(exception)
            ? $"the asset repository does not know it: {string.Join(", ", (exception.Errors ?? []).Select(e => e.Message))}"
            : CkMethodErrorText.Format(exception);

    private static bool IsValidationError(CkMethodCallException exception) =>
        exception.Errors is { Count: > 0 } errors &&
        errors.All(e => e.Extensions != null && e.Extensions.ContainsKey("number"));
}
