using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.Methods;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Operations;

/// <summary>
///     AB#8446: text and JSON forms of an operation of an asynchronous CK method (<see cref="CkOperation" />) for
///     <c>GetOperation</c>, <c>ListOperations</c> and the progress lines of <c>Dump</c>. Stage codes and parameters are
///     printed as the service sends them (language-neutral).
/// </summary>
internal static class OperationText
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary><c>RUNNING, stage DUMPING_DATABASE, 40 % (jobId=…)</c>.</summary>
    public static string Progress(CkOperation operation)
    {
        var text = operation.StateName.Length > 0 ? operation.StateName : operation.State.ToString().ToUpperInvariant();
        if (operation.Progress is { } progress)
        {
            if (!string.IsNullOrWhiteSpace(progress.StageCode))
            {
                text += $", stage {progress.StageCode}";
            }

            if (progress.Percent != null)
            {
                text += $", {progress.Percent.Value.ToString(CultureInfo.InvariantCulture)} %";
            }

            text += Parameters(progress.Parameters);
        }

        return text;
    }

    /// <summary>The lines of one operation (GetOperation).</summary>
    public static IEnumerable<string> Details(CkOperation operation)
    {
        yield return $"Operation:  {operation.OperationId}";
        yield return $"Method:     {operation.MethodId}";
        yield return $"State:      {Progress(operation)}";
        if (operation.InvocationId != null)
        {
            yield return $"Invocation: {operation.InvocationId}";
        }

        yield return $"Accepted:   {Time(operation.AcceptedAtUtc)}";
        if (operation.StartedAtUtc != null)
        {
            yield return $"Started:    {Time(operation.StartedAtUtc)}";
        }

        if (operation.CompletedAtUtc != null)
        {
            yield return $"Completed:  {Time(operation.CompletedAtUtc)}";
        }

        if (operation.ExpiresAtUtc != null)
        {
            yield return $"Expires:    {Time(operation.ExpiresAtUtc)}";
        }

        if (operation.Result is { } result)
        {
            yield return $"Result:     {result.GetRawText()}";
        }

        if (operation.Error is { } error)
        {
            yield return $"Error:      {Error(error)}";
            foreach (var detail in error.Details)
            {
                yield return $"            - {detail.Code}{Parameters(detail.Parameters)}";
            }
        }
    }

    /// <summary><c>DUMP_FAILED (jobId=…)</c>: the code and the parameters of an operation error (no message text).</summary>
    public static string Error(CkMethodError error) => error.Code + Parameters(error.Parameters);

    /// <summary>One line per operation (ListOperations).</summary>
    public static string Row(CkOperation operation) =>
        $"{Time(operation.AcceptedAtUtc)}  {operation.OperationId}  {operation.MethodId}  {Progress(operation)}" +
        (operation.Error != null ? $", error {Error(operation.Error)}" : string.Empty);

    /// <summary>The operation(s) as indented JSON (camelCase, enums as names).</summary>
    public static string Json(object value) => JsonSerializer.Serialize(value, JsonOptions);

    private static string Parameters(IReadOnlyDictionary<string, string> parameters) =>
        parameters.Count == 0
            ? string.Empty
            : " (" + string.Join(", ", parameters.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}")) + ")";

    private static string Time(DateTime? value) =>
        value == null ? "-" : value.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

/// <summary>
///     AB#8446: logs the progress of an operation (state, stage, percent, stage parameters such as <c>jobId</c>); the SDK
///     reports only changes. Synchronous, so the lines keep their order.
/// </summary>
internal sealed class OperationProgressLogger(ILogger logger, string subject) : IProgress<CkOperation>
{
    public void Report(CkOperation value)
    {
        logger.LogInformation("{Subject} \'{OperationId}\': {Progress}", subject, value.OperationId,
            OperationText.Progress(value));
    }
}
