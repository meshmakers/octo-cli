using NLog;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;

namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

/// <summary>
///     Routes every rendered log line of every NLog target (console, log file, debug counters) through
///     <see cref="ErrorOutputRedactor.Redact" /> — AB#8570. This is the one place where octo-cli output is redacted:
///     commands, <see cref="Runner" /> and <see cref="Program" /> log exception messages (which carry server response
///     bodies) as they are, and the layout removes credentials whatever the log level or the configured layout is.
///     A changed or reloaded configuration (e.g. a custom <c>nlog.config</c> with <c>minlevel="Trace"</c>) is wrapped
///     again, so a debug level never brings a header back.
/// </summary>
internal static class RedactedLogOutput
{
    private static readonly Lock Gate = new();
    private static bool _subscribed;

    /// <summary>
    ///     Wraps the layouts of the current configuration and of every later one. Safe to call more than once.
    /// </summary>
    public static void Enable()
    {
        lock (Gate)
        {
            if (!_subscribed)
            {
                LogManager.ConfigurationChanged += (_, e) => Apply(e.ActivatedConfiguration);
                _subscribed = true;
            }
        }

        Apply(LogManager.Configuration);
    }

    /// <summary>
    ///     Wraps the layout of every target of <paramref name="configuration" /> (wrapper targets included) in a
    ///     <see cref="RedactingLayout" />. Layouts that are wrapped already stay as they are.
    /// </summary>
    public static void Apply(LoggingConfiguration? configuration)
    {
        if (configuration == null)
        {
            return;
        }

        var changed = false;
        foreach (var target in configuration.AllTargets.OfType<TargetWithLayout>())
        {
            if (target.Layout is RedactingLayout || target.Layout == null)
            {
                continue;
            }

            target.Layout = new RedactingLayout(target.Layout);
            changed = true;
        }

        if (changed && ReferenceEquals(configuration, LogManager.Configuration))
        {
            LogManager.ReconfigExistingLoggers();
        }
    }

    /// <summary>Renders the inner layout and removes credentials from the result.</summary>
    internal sealed class RedactingLayout(Layout inner) : Layout
    {
        public Layout Inner { get; } = inner;

        protected override string GetFormattedMessage(LogEventInfo logEvent) =>
            ErrorOutputRedactor.Redact(Inner.Render(logEvent));

        public override void Precalculate(LogEventInfo logEvent) => Inner.Precalculate(logEvent);

        public override string ToString() => Inner.ToString() ?? string.Empty;
    }
}
