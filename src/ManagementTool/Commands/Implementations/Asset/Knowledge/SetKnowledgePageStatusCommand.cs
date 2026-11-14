using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Knowledge;

/// <summary>
///     Sets the verification status of a knowledge page (System.Ai <c>KnowledgePage</c>) for a person (AB#8565, decision
///     G2a): <c>Verified</c> is never set over MCP, so this is the human path until the Studio review view exists. Like
///     every status change of the knowledge tools it appends a <c>KnowledgePageVersion</c> with the same content, the user
///     as author and the note; page update and version create run in one GraphQL mutation request, which the asset
///     repository executes in one transaction.
///     <para>
///         AB#8968: the new version takes the next free number (above the current one and every existing version of the
///         page, e.g. a seeded Blueprint version not adopted yet) and records the replaced version as <c>BaseVersion</c>
///         (System.Ai 4.11.0), like the knowledge tools of octo-mcp-service (<c>KnowledgePages.BuildWrite</c>). The page's
///         ancestor chain stays intact, so the platform-update rule (AB#8780) reports no Blueprint version the person
///         already saw. A page a person verified counts as edited: a newer Blueprint version is reported as
///         <c>PlatformUpdateAvailable</c> and adopted by a person, never switched automatically.
///     </para>
///     <para>
///         It needs an interactive user login: a context whose token has no <c>sub</c> claim (client credentials) is
///         refused before anything is sent, because a status decision is a person's decision.
///     </para>
/// </summary>
internal class SetKnowledgePageStatusCommand : ServiceClientOctoCommand<IAssetServicesClient>
{
    public const string CommandName = "SetKnowledgePageStatus";
    internal const int MaxNoteLength = 500;

    private const string PageTypeId = "System.Ai/KnowledgePage";

    internal const string ReadQuery = """
        query knowledgePage($slug: SimpleScalar!) {
          runtime {
            systemAiKnowledgePage(fieldFilter: [{attributePath: "rtWellKnownName", operator: EQUALS, comparisonValue: $slug}]) {
              items {
                rtId rtWellKnownName title kind summary verificationStatus currentVersion
                sections { heading sectionContent sortOrder }
                citations { sectionRef sourceQuote sourceAnchor sourceRtId }
                hallucinationFlags { claim reason }
              }
            }
          }
        }
        """;

    /// <summary>
    ///     The versions of a page (<c>&lt;slug&gt;@&lt;n&gt;</c>), highest number first, and whether the tenant's model has
    ///     <c>KnowledgePageVersion.BaseVersion</c> (System.Ai 4.11.0; the input type of older tenants has no such field).
    /// </summary>
    internal const string HistoryQuery = """
        query knowledgePageVersions($pattern: SimpleScalar!) {
          runtime {
            systemAiKnowledgePageVersion(first: 50, sortOrder: [{attributePath: "versionNumber", sortOrder: DESCENDING}], fieldFilter: [{attributePath: "rtWellKnownName", operator: MATCH_REG_EX, comparisonValue: $pattern}]) {
              items { rtWellKnownName versionNumber }
            }
          }
          versionInput: __type(name: "SystemAiKnowledgePageVersionInput") { inputFields { name } }
        }
        """;

    /// <summary>The version attribute of System.Ai 4.11.0 naming the version a write was based on (AB#8780).</summary>
    internal const string BaseVersionField = "baseVersion";

    // Two root fields: GraphQL runs root mutation fields one after the other, in the request's single transaction.
    internal const string WriteMutation = """
        mutation setKnowledgePageStatus($page: [SystemAiKnowledgePageInputUpdate]!, $version: [SystemAiKnowledgePageVersionInput]!) {
          page: runtime { systemAiKnowledgePages { update(entities: $page) { rtId currentVersion verificationStatus } } }
          version: runtime { systemAiKnowledgePageVersions { create(entities: $version) { rtId rtWellKnownName } } }
        }
        """;

    private readonly IOptions<OctoToolAuthenticationOptions> _authenticationOptions;
    private readonly IArgument _noteArg;
    private readonly IArgument _slugArg;
    private readonly IArgument _statusArg;
    private readonly IArgument _tenantIdArg;
    private readonly ITenantTokenProvider? _tenantTokenProvider;
    private readonly TimeProvider _timeProvider;

    public SetKnowledgePageStatusCommand(ILogger<SetKnowledgePageStatusCommand> logger,
        IOptions<OctoToolOptions> options, IAssetServicesClient assetServicesClient,
        IAuthenticationService authenticationService, IOptions<OctoToolAuthenticationOptions> authenticationOptions,
        ITenantTokenProvider? tenantTokenProvider = null, TimeProvider? timeProvider = null)
        : base(logger, Constants.AssetRepositoryServicesGroup, "SetKnowledgePageStatus", // literal for the command-reference generator
            "Sets the verification status of a knowledge page (Draft, InReview, Verified, Rejected) as the logged-in person " +
            "and records it as a new page version. The only way to set Verified: AI clients over MCP never can.",
            options, assetServicesClient, authenticationService)
    {
        _authenticationOptions = authenticationOptions;
        _tenantTokenProvider = tenantTokenProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;

        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId",
            ["Id of the tenant of the knowledge base. Default: the tenant of the context"], false, 1);
        _slugArg = CommandArgumentValue.AddArgument("s", "slug",
            ["Slug of the knowledge page (its rtWellKnownName), e.g. 'knowledge-schema'"], true, 1);
        _statusArg = CommandArgumentValue.AddArgument("st", "status",
            ["New status: Draft, InReview, Verified or Rejected"], true, 1);
        _noteArg = CommandArgumentValue.AddArgument("n", "note",
            ["Why, e.g. 'checked against the runbook' (stored as the change note of the version, max. 500 characters)"],
            false, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(
                    arguments:
                    [
                        new CodeSampleArgument(_tenantIdArg, "ai-sandbox"),
                        new CodeSampleArgument(_slugArg, "rollout-seed-change"),
                        new CodeSampleArgument(_statusArg, "Verified"),
                        new CodeSampleArgument(_noteArg, "checked against the runbook")
                    ],
                    description: "Verify a knowledge page after reading it (interactive user login required)")
            ],
            Notes:
            [
                "Decision G2a (AB#8565): MCP callers are always AI clients, so knowledge_set_status refuses Verified for every " +
                "caller (VerifiedNotAllowedOverMcp). A person sets Verified with this command, or later in the Refinery Studio " +
                "review view.",
                "Needs an interactive user login (LogIn). A client-credentials context is refused before anything is sent.",
                "Writes the page status and a new KnowledgePageVersion (same content, author user:<sub>, the note) in one " +
                "GraphQL mutation request (one transaction). The note is not checked by the MCP content guard: do not put " +
                "credentials into it.",
                "A plain GraphQL update of verificationStatus is not the documented path: it skips the version.",
                "The new version takes the next free number (above the current version and every existing version of the " +
                "page, e.g. a seeded Blueprint version not adopted yet) and records the replaced version as BaseVersion " +
                "(System.Ai 4.11.0 or newer; older tenants get no BaseVersion), like the MCP knowledge tools.",
                "A page you verified counts as edited by a person: a newer Blueprint version is not adopted automatically " +
                "but reported as PlatformUpdateAvailable (adopt it with knowledge_restore_version, or keep the page with " +
                "knowledge_dismiss_platform_update). Blueprint versions you already saw are not reported again."
            ]);

    public override async Task Execute()
    {
        var tenantId = CommandArgumentValue.IsArgumentUsed(_tenantIdArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_tenantIdArg)
            : Options.Value.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw ToolException.NoTenantIdConfigured();
        }

        tenantId = tenantId.Trim().ToLowerInvariant();
        var slug = CommandArgumentValue.GetArgumentScalarValue<string>(_slugArg).Trim();
        var status = ParseStatus(CommandArgumentValue.GetArgumentScalarValue<string>(_statusArg));
        var note = CommandArgumentValue.IsArgumentUsed(_noteArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_noteArg).Trim()
            : null;
        if (note is { Length: > MaxNoteLength })
        {
            throw new ToolException($"The note is longer than {MaxNoteLength} characters.");
        }

        // A person's decision: refuse a context without a user before anything is sent. The author is the subject of the
        // token the tenant sees (the exchanged token for another tenant), as the knowledge tools record it.
        var subject = await TargetTenantSubjectAsync(tenantId);

        var page = await ReadPageAsync(tenantId, slug);
        var newStatus = KnowledgePageContent.ToConstantCase(status);
        var history = await ReadHistoryAsync(tenantId, slug);
        var versionNumber = Math.Max(page.CurrentVersion, history.HighestVersion) + 1;
        int? baseVersion = history.HasBaseVersion && page.CurrentVersion > 0 ? page.CurrentVersion : null;
        var hash = page.HashWithStatus(newStatus);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var changeNote = string.IsNullOrWhiteSpace(note) ? $"status {status}" : note;

        Logger.LogInformation(
            "Setting knowledge page '{Slug}' in tenant '{TenantId}' from {OldStatus} to {NewStatus} as version {Version}",
            slug, tenantId, KnowledgePageContent.ToPascalCase(page.Status), status, versionNumber);

        var variables = BuildVariables(page, newStatus, hash, versionNumber, $"user:{subject}", changeNote, now,
            baseVersion);
        var response = await ServiceClient.SendGraphQlAsync(tenantId, WriteMutation,
            variables.ToJsonString(), "setKnowledgePageStatus");
        if (response.HasErrors)
        {
            throw new ToolException(
                $"Setting the status of '{slug}' in tenant '{tenantId}' failed (HTTP {(int)response.StatusCode}); nothing " +
                $"was written: {ErrorMessages(response.Content)}");
        }

        // GraphQL has no conditional update: verify afterwards that no knowledge-tool write landed between the read and
        // the write. If one did, the status may now stand on content the person did not review.
        var after = await ReadPageAsync(tenantId, slug);
        if (after.CurrentVersion != versionNumber || after.Status != newStatus || after.HashWithStatus(newStatus) != hash)
        {
            throw new ToolException(
                $"The status of '{slug}' was written as version {versionNumber}, but the page changed at the same time " +
                $"(it is now at version {after.CurrentVersion}, {KnowledgePageContent.ToPascalCase(after.Status)}). The status " +
                "may stand on content you did not review: read the page again and set the status again.");
        }

        Logger.LogInformation("Knowledge page '{Slug}' is {Status} (version {Version})", slug, status, versionNumber);
    }

    private ToolException NoUser() => new(
        $"{CommandName} needs an interactive user login (octo-cli -c LogIn). This context is logged in with client " +
        "credentials (no user): a service account or agent cannot set the status of a knowledge page here.");

    /// <summary>
    ///     The <c>sub</c> of the token the target tenant sees: the context token, or the token exchanged from it for
    ///     another tenant (the same cached exchange the service client uses). Refuses a context without a user.
    /// </summary>
    private async Task<string> TargetTenantSubjectAsync(string tenantId)
    {
        var token = _authenticationOptions.Value.AccessToken;
        if (UserSubject(token) == null)
        {
            throw NoUser();
        }

        if (AccessTokenInspector.IsIssuedForOtherTenant(token!, tenantId))
        {
            if (_tenantTokenProvider == null)
            {
                throw new ToolException($"No token for tenant '{tenantId}' is available; use a context of that tenant.");
            }

            try
            {
                token = await _tenantTokenProvider.GetAccessTokenAsync(tenantId, token!);
            }
            catch (TenantTokenExchangeDeniedException ex)
            {
                throw new ToolException(
                    $"No token for tenant '{tenantId}' could be obtained from the signed-in user: {ex.Message} " +
                    $"Provision yourself there (ProvisionCurrentUser -ttid {tenantId}) or use a context of that tenant.", ex);
            }
        }

        return UserSubject(token) ?? throw NoUser();
    }

    internal static string ParseStatus(string value)
    {
        var name = KnowledgePageContent.StatusNames.FirstOrDefault(n =>
            n.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase) ||
            KnowledgePageContent.ToConstantCase(n).Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
        return name ?? throw new ToolException(
            $"Unknown status '{value}'. Use one of: {string.Join(", ", KnowledgePageContent.StatusNames)}.");
    }

    /// <summary>The <c>sub</c> claim of a JWT access token, or <c>null</c> (no token, not a JWT, no user).</summary>
    internal static string? UserSubject(string? accessToken)
    {
        var parts = accessToken?.Split('.');
        if (parts is not { Length: 3 })
        {
            return null;
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            return doc.RootElement.TryGetProperty("sub", out var sub) && sub.ValueKind == JsonValueKind.String &&
                   !string.IsNullOrWhiteSpace(sub.GetString())
                ? sub.GetString()
                : null;
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return null;
        }
    }

    internal static JsonObject BuildVariables(KnowledgePageContent page, string newStatus, string hash,
        int versionNumber, string author, string changeNote, DateTime now, int? baseVersion = null)
    {
        var timestamp = now.ToString("O", CultureInfo.InvariantCulture);
        var variables = new JsonObject
        {
            ["page"] = new JsonArray(new JsonObject
            {
                ["rtId"] = page.RtId,
                ["item"] = new JsonObject
                {
                    ["verificationStatus"] = newStatus,
                    ["contentHash"] = hash,
                    ["currentVersion"] = versionNumber,
                    ["compiledAt"] = timestamp
                }
            }),
            ["version"] = new JsonArray(new JsonObject
            {
                ["rtWellKnownName"] = $"{page.Slug}@{versionNumber}",
                ["versionNumber"] = versionNumber,
                ["title"] = page.Title,
                ["kind"] = page.Kind,
                ["summary"] = page.Summary ?? string.Empty,
                ["sections"] = Copy(page.Sections),
                ["citations"] = Copy(page.Citations),
                ["hallucinationFlags"] = Copy(page.HallucinationFlags),
                ["verificationStatus"] = newStatus,
                ["contentHash"] = hash,
                ["author"] = author,
                ["authoredAt"] = timestamp,
                ["changeNote"] = changeNote,
                ["versionOf"] = new JsonArray(new JsonObject
                {
                    ["target"] = new JsonObject { ["ckTypeId"] = PageTypeId, ["rtId"] = page.RtId },
                    ["modOption"] = "CREATE"
                })
            })
        };
        if (baseVersion != null)
        {
            variables["version"]![0]![BaseVersionField] = baseVersion.Value;
        }

        return variables;
    }

    /// <summary>
    ///     The highest version number of the page (0 when it has none) and whether the tenant's model records
    ///     <c>BaseVersion</c>. A version counts with the higher of its <c>versionNumber</c> and the number in its name (the
    ///     name is what must stay unique); the server returns the 50 highest by <c>versionNumber</c>.
    /// </summary>
    private async Task<(int HighestVersion, bool HasBaseVersion)> ReadHistoryAsync(string tenantId, string slug)
    {
        var variables = new JsonObject { ["pattern"] = VersionNamePattern(slug) }.ToJsonString();
        var response = await ServiceClient.SendGraphQlAsync(tenantId, HistoryQuery, variables, "knowledgePageVersions");
        if (response.HasErrors)
        {
            throw new ToolException(
                $"Reading the versions of knowledge page '{slug}' in tenant '{tenantId}' failed " +
                $"(HTTP {(int)response.StatusCode}); nothing was written: {ErrorMessages(response.Content)}");
        }

        using var doc = JsonDocument.Parse(response.Content);
        var data = doc.RootElement.GetProperty("data");
        return (HighestVersion(data, slug), HasBaseVersion(data));
    }

    /// <summary>The regular expression of the version names of a page: <c>^&lt;slug&gt;@[0-9]+$</c>, the slug escaped.</summary>
    internal static string VersionNamePattern(string slug) => $"^{Regex.Escape(slug)}@[0-9]+$";

    internal static int HighestVersion(JsonElement data, string slug)
    {
        var highest = 0;
        if (!data.TryGetProperty("runtime", out var runtime) ||
            !runtime.TryGetProperty("systemAiKnowledgePageVersion", out var connection) ||
            !connection.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return highest;
        }

        var prefix = slug + "@";
        foreach (var item in items.EnumerateArray())
        {
            // The filter is a regular expression on the server; only exact names of this page count here.
            if (!item.TryGetProperty("rtWellKnownName", out var nameElement) ||
                nameElement.GetString() is not { } name || !name.StartsWith(prefix, StringComparison.Ordinal) ||
                !int.TryParse(name[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var fromName))
            {
                continue;
            }

            var number = item.TryGetProperty("versionNumber", out var n) && n.ValueKind == JsonValueKind.Number
                ? n.GetInt32()
                : fromName;
            highest = Math.Max(highest, Math.Max(number, fromName));
        }

        return highest;
    }

    internal static bool HasBaseVersion(JsonElement data) =>
        data.TryGetProperty("versionInput", out var type) && type.ValueKind == JsonValueKind.Object &&
        type.TryGetProperty("inputFields", out var fields) && fields.ValueKind == JsonValueKind.Array &&
        fields.EnumerateArray().Any(f =>
            f.TryGetProperty("name", out var name) && name.GetString() == BaseVersionField);

    private async Task<KnowledgePageContent> ReadPageAsync(string tenantId, string slug)
    {
        var variables = new JsonObject { ["slug"] = slug }.ToJsonString();
        var response = await ServiceClient.SendGraphQlAsync(tenantId, ReadQuery, variables, "knowledgePage");
        if (response.HasErrors)
        {
            throw new ToolException(
                $"Reading knowledge page '{slug}' in tenant '{tenantId}' failed (HTTP {(int)response.StatusCode}): " +
                ErrorMessages(response.Content));
        }

        using var doc = JsonDocument.Parse(response.Content);
        var items = doc.RootElement.GetProperty("data").GetProperty("runtime").GetProperty("systemAiKnowledgePage")
            .GetProperty("items");
        var matches = items.EnumerateArray()
            .Where(i => i.TryGetProperty("rtWellKnownName", out var n) && n.GetString() == slug)
            .ToList();
        return matches.Count switch
        {
            0 => throw new ToolException($"Knowledge page '{slug}' not found in tenant '{tenantId}'."),
            1 => KnowledgePageContent.FromGraphQl(matches[0]),
            _ => throw new ToolException(
                $"Tenant '{tenantId}' has {matches.Count} knowledge pages with the slug '{slug}'; resolve the duplicate first.")
        };
    }

    private static JsonArray Copy(IReadOnlyList<JsonObject> records) =>
        new(records.Select(r => (JsonNode?)JsonNode.Parse(r.ToJsonString())).ToArray());

    private static string ErrorMessages(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                return string.Join("; ", errors.EnumerateArray()
                    .Select(e => e.TryGetProperty("message", out var m) ? m.GetString() : null)
                    .Where(m => !string.IsNullOrWhiteSpace(m)));
            }
        }
        catch (JsonException)
        {
            // Not JSON: fall through.
        }

        return "the service returned an error";
    }
}
