using System.Globalization;
using System.Text.Json.Nodes;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Knowledge;

/// <summary>
///     Adopts the platform updates of a tenant's knowledge pages right away, through the MCP tool
///     <c>knowledge_adopt_platform_updates</c> with the signed-in user's token: every page that was never edited in the
///     tenant is switched to its newest Blueprint version; edited pages are never changed. The AI service does the same by
///     itself every 15 minutes; this command is for an operator who does not want to wait (also where agents have no MCP
///     client). <c>--dryRun</c> calls only <c>knowledge_list_platform_updates</c> and writes nothing.
/// </summary>
internal class AdoptKnowledgePlatformUpdatesCommand : ServiceClientOctoCommand<IAssetServicesClient>
{
    /// <summary>Longest run reference the MCP tool accepts.</summary>
    internal const int MaxRunRefLength = 200;

    private readonly IOptions<OctoToolAuthenticationOptions> _authenticationOptions;
    private readonly IConsoleService _consoleService;
    private readonly IArgument _dryRunArg;
    private readonly IArgument _jsonArg;
    private readonly IMcpToolClient _mcpToolClient;
    private readonly IArgument _runRefArg;
    private readonly IArgument _tenantIdArg;
    private readonly ITenantTokenProvider? _tenantTokenProvider;
    private readonly TimeProvider _timeProvider;

    public AdoptKnowledgePlatformUpdatesCommand(ILogger<AdoptKnowledgePlatformUpdatesCommand> logger,
        IOptions<OctoToolOptions> options, IAssetServicesClient assetServicesClient,
        IAuthenticationService authenticationService, IOptions<OctoToolAuthenticationOptions> authenticationOptions,
        IMcpToolClient mcpToolClient, IConsoleService consoleService, ITenantTokenProvider? tenantTokenProvider = null,
        TimeProvider? timeProvider = null)
        : base(logger, Constants.AssetRepositoryServicesGroup, "AdoptKnowledgePlatformUpdates", // literal for the command-reference generator
            "Adopts newer Blueprint versions of knowledge pages now: every page never edited in the tenant switches to its " +
            "newest Blueprint version; edited pages are never changed. The AI service does this by itself every 15 minutes.",
            options, assetServicesClient, authenticationService)
    {
        _authenticationOptions = authenticationOptions;
        _mcpToolClient = mcpToolClient;
        _consoleService = consoleService;
        _tenantTokenProvider = tenantTokenProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;

        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId",
            ["Id of the tenant of the knowledge base. Default: the tenant of the context"], false, 1);
        _runRefArg = CommandArgumentValue.AddArgument("rr", "runRef",
            ["Reference stored on every switched page as AdoptedByRun, e.g. a change or ticket id (max. 200 characters). " +
             "Default: 'octo-cli:<UTC time>'"], false, 1);
        _dryRunArg = CommandArgumentValue.AddArgument("dr", "dryRun",
            ["Change nothing: list the pages that would be switched and the edited pages that keep their content"], false, 0);
        _jsonArg = CommandArgumentValue.AddArgument("json", "json",
            ["Print the answer of the MCP tool as JSON instead of a summary"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_tenantIdArg, "ai-sandbox"), new CodeSampleArgument(_dryRunArg)],
                    description: "See which unedited pages would be switched, without writing"),
                new CodeSample(arguments: [new CodeSampleArgument(_tenantIdArg, "ai-sandbox"),
                        new CodeSampleArgument(_runRefArg, "rollout-builder-1.2.0")],
                    description: "Adopt the newer Blueprint versions right after a blueprint update")
            ],
            Notes:
            [
                "Not needed for normal operation: the AI service adopts unedited pages of every AI tenant by itself every 15 " +
                "minutes. Use this command to adopt immediately, e.g. right after a blueprint update during a rollout.",
                "Same rules as the MCP tool knowledge_adopt_platform_updates, because the command calls that tool: only a page " +
                "whose current version is a Blueprint version (never edited in the tenant) is switched, without writing a new " +
                "version; it takes title, summary, sections, citations, status and the declared links of the newest Blueprint " +
                "version. Edited pages are listed as Available and keep their content.",
                "Calls <MCP service URI>/<tenant>/mcp with the token of the context (exchanged for another tenant like every " +
                "command); the MCP service needs no MCP client setup on your side. The URI comes only from the context " +
                "(AddContext -msu or Config -msu): there is no per-call override, so your token goes only to the MCP " +
                "service of the context.",
                "If the tool fails part-way, the pages it already switched are printed before the error (one transaction " +
                "per page); run the command again for the rest.",
                "A second run without a new blueprint version writes nothing."
            ]);

    public override async Task Execute()
    {
        var tenantId = KnowledgePlatformUpdatesCommandSupport.TenantId(CommandArgumentValue, _tenantIdArg, Options.Value);
        var dryRun = CommandArgumentValue.IsArgumentUsed(_dryRunArg);
        var runRef = CommandArgumentValue.IsArgumentUsed(_runRefArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_runRefArg).Trim()
            : DefaultRunRef(_timeProvider.GetUtcNow());
        if (runRef.Length is 0 or > MaxRunRefLength)
        {
            throw new ToolException($"--runRef must hold 1 to {MaxRunRefLength} characters.");
        }

        var endpoint = KnowledgePlatformUpdatesMcp.Endpoint(Options.Value, tenantId);
        var token = await KnowledgePlatformUpdatesMcp.TokenForTenantAsync(_authenticationOptions.Value.AccessToken, tenantId,
            _tenantTokenProvider);

        if (dryRun)
        {
            Logger.LogInformation("Dry run: listing knowledge platform updates of tenant '{TenantId}' at {Endpoint}; nothing is written",
                tenantId, endpoint);
            var list = KnowledgePlatformUpdatesMcp.RequireSuccess(
                await _mcpToolClient.CallToolAsync(endpoint, token, KnowledgePlatformUpdatesMcp.ListTool,
                    new JsonObject { ["tenantId"] = tenantId }), KnowledgePlatformUpdatesMcp.ListTool);
            if (CommandArgumentValue.IsArgumentUsed(_jsonArg))
            {
                _consoleService.WriteLine(list.ToJsonString());
                return;
            }

            ListKnowledgePlatformUpdatesCommand.Print(_consoleService, tenantId, list);
            _consoleService.WriteLine("Dry run: nothing was written. Without --dryRun the Adoptable pages are switched.");
            return;
        }

        Logger.LogInformation("Adopting knowledge platform updates of tenant '{TenantId}' at {Endpoint} (runRef '{RunRef}')",
            tenantId, endpoint, runRef);
        var answer = KnowledgePlatformUpdatesMcp.AnswerObject(
            await _mcpToolClient.CallToolAsync(endpoint, token, KnowledgePlatformUpdatesMcp.AdoptTool,
                new JsonObject { ["runRef"] = runRef, ["tenantId"] = tenantId }), KnowledgePlatformUpdatesMcp.AdoptTool);
        var success = KnowledgePlatformUpdatesMcp.IsSuccess(answer);
        var switched = KnowledgePlatformUpdatesMcp.Items(answer, "adopted").Count();
        var touched = switched + KnowledgePlatformUpdatesMcp.Items(answer, "skipped").Count();
        if (!success && touched == 0)
        {
            throw KnowledgePlatformUpdatesMcp.Refused(answer, KnowledgePlatformUpdatesMcp.AdoptTool,
                "Nothing was changed by this command.");
        }

        // A run that failed part-way has already committed the pages in "adopted" (one transaction per page): show them
        // before the error instead of claiming that nothing changed.
        if (CommandArgumentValue.IsArgumentUsed(_jsonArg))
        {
            _consoleService.WriteLine(answer.ToJsonString());
        }
        else
        {
            Print(_consoleService, tenantId, runRef, answer);
        }

        if (!success)
        {
            throw KnowledgePlatformUpdatesMcp.Refused(answer, KnowledgePlatformUpdatesMcp.AdoptTool,
                $"{switched} page(s) were switched before the failure (listed above, AdoptedByRun '{runRef}'); run the " +
                "command again for the rest.");
        }
    }

    /// <summary>The run reference when <c>--runRef</c> is not given: <c>octo-cli:2026-11-24T15:30:00Z</c>.</summary>
    internal static string DefaultRunRef(DateTimeOffset now) =>
        "octo-cli:" + now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static void Print(IConsoleService console, string tenantId, string runRef, JsonObject answer)
    {
        var adopted = KnowledgePlatformUpdatesMcp.Items(answer, "adopted").ToList();
        var skipped = KnowledgePlatformUpdatesMcp.Items(answer, "skipped").ToList();
        var available = KnowledgePlatformUpdatesMcp.Items(answer, "available").ToList();
        console.WriteLine($"Tenant '{tenantId}' (runRef '{runRef}'): adopted {adopted.Count} page(s), skipped {skipped.Count}; " +
                          $"{available.Count} edited page(s) keep their content.");
        foreach (var page in adopted)
        {
            console.WriteLine($"  ADOPTED   {KnowledgePlatformUpdatesMcp.Text(page, "slug")}  " +
                              $"v{KnowledgePlatformUpdatesMcp.Number(page, "fromVersion")} -> v{KnowledgePlatformUpdatesMcp.Number(page, "toVersion")}  " +
                              $"{KnowledgePlatformUpdatesMcp.Text(page, "blueprint")}");
        }

        foreach (var page in skipped)
        {
            console.WriteLine($"  SKIPPED   {KnowledgePlatformUpdatesMcp.Text(page, "slug")}  " +
                              $"v{KnowledgePlatformUpdatesMcp.Number(page, "fromVersion")} -> v{KnowledgePlatformUpdatesMcp.Number(page, "toVersion")}  " +
                              $"{KnowledgePlatformUpdatesMcp.Text(page, "reason")}");
        }

        foreach (var page in available)
        {
            console.WriteLine($"  AVAILABLE {KnowledgePlatformUpdatesMcp.Text(page, "slug")}  " +
                              $"v{KnowledgePlatformUpdatesMcp.Number(page, "currentVersion")}, newer v{KnowledgePlatformUpdatesMcp.Number(page, "newerVersion")} " +
                              "(edited in the tenant: restore the version or dismiss it)");
        }

        if (KnowledgePlatformUpdatesMcp.Flag(answer, "truncated"))
        {
            console.WriteLine("Not every Blueprint version was read (safety cap of the MCP service): run the command again.");
        }
    }
}
