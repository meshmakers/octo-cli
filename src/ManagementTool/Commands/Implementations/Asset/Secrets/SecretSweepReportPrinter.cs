using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Secrets;

/// <summary>
///     Renders secret sweep reports of the bot service (AB#5543). The reports carry counts, key ids and entity
///     references only — never secret values — and this printer adds nothing else.
/// </summary>
internal static class SecretSweepReportPrinter
{
    private const string SlotFormat = "{CkType,-55} {Path,-28} {NotSet,7} {Ph,5} {Plain,6} {V1,6} {V2,6} {Unknown,8} {Failed,6}";

    /// <summary>Allowed sweep modes, by name (the emergency Decrypt mode is not offered).</summary>
    public static readonly SecretSweepModeDto[] OfferedModes =
    [
        SecretSweepModeDto.Verify, SecretSweepModeDto.Encrypt, SecretSweepModeDto.Reprotect,
        SecretSweepModeDto.CleanupUnreadable
    ];

    /// <summary>Parses a sweep mode by name (case-insensitive); numbers and Decrypt are refused.</summary>
    public static SecretSweepModeDto ParseMode(string? mode, SecretSweepModeDto defaultMode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return defaultMode;
        }

        var name = Enum.GetNames<SecretSweepModeDto>()
            .FirstOrDefault(n => string.Equals(n, mode.Trim(), StringComparison.OrdinalIgnoreCase));
        if (name == null)
        {
            throw ToolException.InvalidSecretSweepMode(mode);
        }

        var parsed = Enum.Parse<SecretSweepModeDto>(name);
        if (!OfferedModes.Contains(parsed))
        {
            throw ToolException.InvalidSecretSweepMode(mode);
        }

        return parsed;
    }

    /// <summary>Prints one tenant report in detail.</summary>
    public static void PrintReport(ILogger logger, SecretSweepReportDto report)
    {
        logger.LogInformation(
            "Tenant '{TenantId}': last sweep {Mode} ({Trigger}) completed {CompletedAt:u} with outcome {Outcome}",
            report.TenantId, report.Mode, report.Trigger, report.CompletedAt, report.Outcome);
        if (!string.IsNullOrWhiteSpace(report.Reason))
        {
            logger.LogInformation("Reason: {Reason}", report.Reason);
        }

        logger.LogInformation(
            "Active key id: {ActiveKeyId}, strict mode: {StrictMode}, remaining legacy values: {Legacy}, legacy " +
            "placeholders normalised: {Normalized}",
            report.ActiveKeyId ?? "<none>", report.StrictModeActive ? "on" : "off", report.RemainingLegacyValues,
            report.PlaceholdersNormalized);
        if (!string.IsNullOrWhiteSpace(report.BackupFileName))
        {
            logger.LogInformation("Pre-sweep backup: {BackupFileName}", report.BackupFileName);
        }

        if (report.StrictModeViolation)
        {
            logger.LogWarning(
                "Strict mode violation: legacy (plaintext / enc:v1) values were found while strict mode is active");
        }

        foreach (var step in report.Steps)
        {
            var totals = step.Totals;
            logger.LogInformation(
                "Step {Mode} ({Result}): {Entities} entities scanned, {Rewritten} values rewritten, {Normalized} " +
                "placeholders normalised, {Skipped} skipped (concurrently modified)",
                step.Mode, step.Success ? "ok" : "with failures", step.EntitiesScanned, step.ValuesRewritten,
                step.PlaceholdersNormalized, step.SkippedConcurrentlyModified);
            logger.LogInformation(
                "  Totals: total={Total} notSet={NotSet} legacyPlaceholder={Placeholder} plaintext={Plaintext} encV1={EncV1} " +
                "encV2={EncV2} unknownKeyId={UnknownKeyId} failed={Failed}",
                totals.Total, totals.NotSet, totals.Placeholder, totals.Plaintext, totals.EncV1, totals.EncV2,
                totals.UnknownKeyId, totals.Failed);
            foreach (var (kid, count) in totals.EncV2ByKeyId.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                logger.LogInformation("  encV2 key id {KeyId}: {Count}", kid, count);
            }

            foreach (var (kid, count) in totals.UnknownKeyIdByKeyId.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                logger.LogWarning("  unknown key id {KeyId}: {Count}", kid, count);
            }

            if (step.Slots.Count > 0)
            {
                logger.LogInformation(SlotFormat, "CK TYPE", "ATTRIBUTE", "NOT SET", "PH", "PLAIN", "ENCV1", "ENCV2",
                    "UNKNOWN", "FAILED");
                foreach (var slot in step.Slots.OrderBy(s => s.CkTypeId, StringComparer.Ordinal)
                             .ThenBy(s => s.AttributePath, StringComparer.Ordinal))
                {
                    var c = slot.Counts;
                    logger.LogInformation(SlotFormat, slot.CkTypeId, slot.AttributePath, c.NotSet, c.Placeholder,
                        c.Plaintext, c.EncV1, c.EncV2, c.UnknownKeyId, c.Failed);
                }
            }

            foreach (var failure in step.Failures)
            {
                logger.LogWarning("  Failure: {CkTypeId} {RtId} {AttributePath}: {Reason}", failure.CkTypeId,
                    failure.RtId, failure.AttributePath, failure.Reason);
            }
        }

        if (report.Unreadable.Count > 0)
        {
            logger.LogWarning(
                "{Count} unreadable secret(s) (stored, but the key id is not in the key ring) — re-enter them, add " +
                "the key to the key ring, or remove them with ReprotectSecrets -m CleanupUnreadable -y:",
                report.Unreadable.Count);
            foreach (var secret in report.Unreadable)
            {
                logger.LogWarning("  {CkTypeId} {RtId} {AttributePath} (key id {KeyId})", secret.CkTypeId,
                    secret.RtId, secret.AttributePath, secret.KeyId ?? "-");
            }
        }
        else
        {
            logger.LogInformation("Unreadable secrets: none");
        }

        if (report.SecretsToReEnter.Count > 0)
        {
            logger.LogWarning("{Count} secret(s) must be re-entered (removed by CleanupUnreadable):",
                report.SecretsToReEnter.Count);
            foreach (var secret in report.SecretsToReEnter)
            {
                logger.LogWarning("  {CkTypeId} {RtId} {AttributePath} (was {PreviousForm}, key id {KeyId})",
                    secret.CkTypeId, secret.RtId, secret.AttributePath, secret.PreviousForm, secret.KeyId ?? "-");
            }
        }
    }

    /// <summary>Prints the environment-level encryption status (identical in every tenant, plus this tenant's last Verify).</summary>
    public static void PrintEnvironment(ILogger logger, SecretEnvironmentStatusDto status)
    {
        if (!status.KeyRingConfigured)
        {
            logger.LogWarning(
                "Key ring: NOT configured — writing a secret fails with SecretEncryptionNotConfigured");
        }
        else
        {
            logger.LogInformation("Key ring: configured, active key id {ActiveKeyId}, known key ids {KnownKeyIds}",
                status.ActiveKeyId ?? "<none>", string.Join(", ", status.KnownKeyIds));
        }

        logger.LogInformation("Legacy enc:v1 key: {Legacy}", status.LegacyV1KeyConfigured ? "configured" : "not configured");
        logger.LogInformation("Strict mode: {StrictMode}{Since}", status.StrictMode ? "on" : "off",
            status.StrictModeSince is { } since ? $" (since {since:u})" : string.Empty);
        logger.LogInformation("Recurring Verify: {Cron}, last Verify: {LastVerify}",
            status.RecurringVerifyCron ?? "disabled",
            status.LastVerifyAt is { } at ? at.ToString("u") : "never");
        logger.LogInformation("Key ids needed by encrypted dumps: {RequiredKeyIds}",
            status.RequiredKeyIds.Count > 0 ? string.Join(", ", status.RequiredKeyIds) : "none");
        if (status.Warnings.Contains(SecretEnvironmentWarningCodes.DumpKeyMissing))
        {
            var missing = status.RequiredKeyIds.Where(k => !status.KnownKeyIds.Contains(k)).ToList();
            logger.LogWarning(
                "DumpKeyMissing: an encrypted dump needs key id(s) {MissingKeyIds} that are not in the key ring — " +
                "it can neither be restored nor downloaded until the key is put back into SecretEncryption:Keys",
                missing.Count > 0 ? string.Join(", ", missing) : "<unknown>");
        }
    }

    /// <summary>Prints the recent sweep runs, newest first, with the state of their pre-sweep dump.</summary>
    public static void PrintRuns(ILogger logger, IReadOnlyCollection<SecretSweepRunDto> runs)
    {
        if (runs.Count == 0)
        {
            logger.LogInformation("Sweep runs: none");
            return;
        }

        const string format = "{RunId,-26} {Mode,-18} {Trigger,-10} {Outcome,-22} {Started,-20} {Unreadable,10} {Dump}";
        logger.LogInformation("{Count} recent sweep run(s):", runs.Count);
        logger.LogInformation(format, "RUN ID", "MODE", "TRIGGER", "OUTCOME", "STARTED", "UNREADABLE", "DUMP");
        foreach (var run in runs)
        {
            logger.LogInformation(format, run.RunId, run.Mode, run.Trigger, run.Outcome,
                run.StartedAt.ToString("u"), run.UnreadableCount, DescribeDump(run.Dump));
        }
    }

    internal static string DescribeDump(SecretSweepDumpDto? dump)
    {
        if (dump == null)
        {
            return "-";
        }

        if (dump.DeletedAt is { } deletedAt)
        {
            return $"deleted {deletedAt:u}" + (dump.DeletedBy != null ? $" by {dump.DeletedBy}" : string.Empty);
        }

        return dump.Exists
            ? $"{dump.FileName} ({dump.SizeBytes?.ToString() ?? "?"} bytes, expires {dump.ExpiresAt:u})"
            : $"{dump.FileName} (missing)";
    }

    /// <summary>Prints one line per tenant report.</summary>
    public static void PrintSummary(ILogger logger, IReadOnlyCollection<SecretSweepReportDto> reports)
    {
        const string format = "{Tenant,-25} {Mode,-18} {Trigger,-10} {Outcome,-22} {Plain,6} {Legacy,7} {Strict,-10} {Unreadable,10} {ReEnter,8}";
        logger.LogInformation("{Count} tenant report(s):", reports.Count);
        logger.LogInformation(format, "TENANT", "MODE", "TRIGGER", "OUTCOME", "PLAIN", "LEGACY", "STRICT", "UNREADABLE", "RE-ENTER");
        foreach (var report in reports.OrderBy(r => r.TenantId, StringComparer.Ordinal))
        {
            var strict = report.StrictModeViolation ? "VIOLATION" : report.StrictModeActive ? "on" : "off";
            logger.LogInformation(format, report.TenantId, report.Mode, report.Trigger, report.Outcome,
                report.Steps.LastOrDefault()?.Totals.Plaintext ?? 0, report.RemainingLegacyValues, strict,
                report.Unreadable.Count, report.SecretsToReEnter.Count);
        }

        var violations = reports.Count(r => r.StrictModeViolation);
        if (violations > 0)
        {
            logger.LogWarning("{Count} tenant(s) report a strict mode violation", violations);
        }

        var reEnter = reports.Sum(r => r.SecretsToReEnter.Count + r.Unreadable.Count);
        if (reEnter > 0)
        {
            logger.LogWarning(
                "{Count} secret(s) must be re-entered; run SecretStatus -tid <tenant> for the list", reEnter);
        }
    }
}
