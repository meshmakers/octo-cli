using System.Text;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Blueprints;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Blueprints;

/// <summary>
///     AB#6316: shared parsing and output of the blanking report of a blueprint update (server contract
///     AB#6315). A blueprint seed that carries an empty value, or omits an attribute, would clear what a
///     tenant has entered; the service keeps such values unless the operator confirms. The report holds
///     kind and size summaries only - never values, they may be credentials - and so does everything
///     this class prints.
/// </summary>
internal static class BlueprintBlanking
{
    private static readonly string[] TableHeader = ["Entity (rtId)", "Type", "Attribute", "Reason", "Current", "Incoming"];

    /// <summary>
    ///     Parses the value of <c>--confirm-blanking</c>: <c>&lt;rtId&gt;:&lt;attribute&gt;</c>, split at the
    ///     first colon (a runtime id never contains one). Both parts are mandatory - a half-empty
    ///     confirmation must never widen into "confirm everything".
    /// </summary>
    public static BlueprintBlankingConfirmationDto ParseConfirmation(string value)
    {
        var separator = value.IndexOf(':');
        var rtId = separator > 0 ? value[..separator].Trim() : string.Empty;
        var attributeName = separator > 0 ? value[(separator + 1)..].Trim() : string.Empty;
        if (rtId.Length == 0 || attributeName.Length == 0)
        {
            throw new ToolException(
                $"Invalid --confirm-blanking value '{value}'. Expected '<rtId>:<attribute>', for example " +
                "'65a1b2c3d4e5f60718293a4b:configuration' - copy both parts from PreviewBlueprintUpdate.");
        }

        return new BlueprintBlankingConfirmationDto { RtId = rtId, AttributeName = attributeName };
    }

    /// <summary>
    ///     Renders the report as an aligned text table, one line per attribute, header first. Empty when
    ///     there is nothing to report.
    /// </summary>
    public static IReadOnlyList<string> FormatTable(IReadOnlyCollection<BlueprintBlankedAttributeDto> blanked)
    {
        if (blanked.Count == 0)
        {
            return [];
        }

        var rows = blanked
            .Select(b => new[]
            {
                b.RtId, b.CkTypeId, b.AttributeName, b.Reason, b.CurrentSummary ?? "-", b.IncomingSummary ?? "-"
            })
            .Prepend(TableHeader)
            .ToList();

        var widths = Enumerable.Range(0, TableHeader.Length).Select(i => rows.Max(r => r[i].Length)).ToArray();
        return rows.Select(r => FormatRow(r, widths)).ToList();
    }

    /// <summary>The <c>--confirm-blanking</c> argument that would confirm this attribute.</summary>
    public static string ConfirmationArgument(BlueprintBlankedAttributeDto blanked) =>
        $"--confirm-blanking {blanked.RtId}:{blanked.AttributeName}";

    public static bool SameAttribute(BlueprintBlankingConfirmationDto confirmation, BlueprintBlankedAttributeDto blanked) =>
        string.Equals(confirmation.RtId, blanked.RtId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(confirmation.AttributeName, blanked.AttributeName, StringComparison.OrdinalIgnoreCase);

    private static string FormatRow(string[] cells, int[] widths)
    {
        var line = new StringBuilder("  ");
        for (var i = 0; i < cells.Length; i++)
        {
            line.Append(cells[i].PadRight(widths[i])).Append("  ");
        }

        return line.ToString().TrimEnd();
    }
}
