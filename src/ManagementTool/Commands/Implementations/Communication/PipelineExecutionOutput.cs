using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;

/// <summary>
///     JSON output of the pipeline execution commands (AB#8156). <c>FailureKind</c> (AB#8037) is printed as its name
///     (<c>"OutcomeUnknown"</c>, <c>"Failed"</c>) instead of the enum number, so an operator reads at once whether a
///     failed execution was an unacknowledged write. It is <c>null</c> for an execution that did not fail, for a failure
///     whose adapter reported no kind, and for interrupted or offline-buffered executions. Every other member keeps its
///     previous rendering.
/// </summary>
internal static class PipelineExecutionOutput
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        Converters = { new FailureKindConverter() }
    };

    public static string Serialize(object? result, bool indented) =>
        JsonConvert.SerializeObject(result, indented ? Formatting.Indented : Formatting.None, Settings);

    private sealed class FailureKindConverter : StringEnumConverter
    {
        public override bool CanConvert(Type objectType) =>
            (Nullable.GetUnderlyingType(objectType) ?? objectType) == typeof(PipelineExecutionFailureKind);
    }
}
