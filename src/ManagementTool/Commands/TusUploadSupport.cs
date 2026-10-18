using System.Diagnostics;
using System.Globalization;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands;

/// <summary>
///     Shared by the commands that upload a file to the bot service in parts (Restore, ImportArchiveData; AB#6970).
/// </summary>
internal static class TusUploadSupport
{
    private const long MiB = 1024 * 1024;

    /// <summary>
    ///     Applies the <c>--part-size-mib</c> value to the bot service client options the SDK client reads at upload
    ///     time. The range check is the SDK's; a rejected value is reported as a tool error.
    /// </summary>
    public static void ApplyPartSize(IOptions<BotServiceClientOptions> botOptions, int partSizeMib)
    {
        try
        {
            botOptions.Value.TusPartSizeBytes = partSizeMib * MiB;
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ToolException(
                $"The part size {partSizeMib} MiB is not accepted. Use a value from " +
                $"{BotServiceClientOptions.MinTusPartSizeBytes / MiB} to " +
                $"{BotServiceClientOptions.MaxTusPartSizeBytes / MiB} MiB (default " +
                $"{BotServiceClientOptions.DefaultTusPartSizeBytes / MiB} MiB); the ingress of the managed " +
                "environments refuses a request body above 100 MiB.");
        }
    }

    public static string FormatSize(long bytes)
    {
        return bytes switch
        {
            >= 1024 * MiB => string.Create(CultureInfo.InvariantCulture, $"{(double)bytes / (1024 * MiB):0.##} GiB"),
            >= MiB => string.Create(CultureInfo.InvariantCulture, $"{(double)bytes / MiB:0.#} MiB"),
            _ => $"{bytes} B"
        };
    }
}

/// <summary>
///     Turns the many progress callbacks of an upload of several GB into a few useful lines: one per 5 percent, or
///     after 30 seconds without a line, with the amount sent and the average rate.
/// </summary>
internal sealed class UploadProgressLogger
{
    private static readonly TimeSpan MaxQuietTime = TimeSpan.FromSeconds(30);
    private const double PercentStep = 5;

    private readonly ILogger _logger;
    private readonly long _totalBytes;
    private readonly Func<TimeSpan> _elapsed;
    private double _lastLoggedPercent;
    private TimeSpan _lastLoggedAt;

    public UploadProgressLogger(ILogger logger, long totalBytes, Func<TimeSpan>? elapsed = null)
    {
        _logger = logger;
        _totalBytes = totalBytes;
        if (elapsed is null)
        {
            var stopwatch = Stopwatch.StartNew();
            elapsed = () => stopwatch.Elapsed;
        }

        _elapsed = elapsed;
    }

    public void Report(double fraction)
    {
        fraction = Math.Clamp(fraction, 0d, 1d);
        var percent = fraction * 100;
        var now = _elapsed();
        var finished = fraction >= 1d;

        // Nothing new, or too soon after the last line: one line per 5 percent, or after 30 quiet seconds.
        var advanced = percent > _lastLoggedPercent;
        var due = percent >= _lastLoggedPercent + PercentStep || now - _lastLoggedAt >= MaxQuietTime || finished;
        if (!advanced || !due)
        {
            return;
        }

        _lastLoggedPercent = percent;
        _lastLoggedAt = now;

        var sent = (long)(_totalBytes * fraction);
        var rate = now.TotalSeconds > 0 ? (long)(sent / now.TotalSeconds) : 0;
        _logger.LogInformation("Upload progress: {Percent:0}% ({Sent} of {Total}, {Rate}/s)", Math.Floor(percent),
            TusUploadSupport.FormatSize(sent), TusUploadSupport.FormatSize(_totalBytes),
            TusUploadSupport.FormatSize(rate));
    }
}
