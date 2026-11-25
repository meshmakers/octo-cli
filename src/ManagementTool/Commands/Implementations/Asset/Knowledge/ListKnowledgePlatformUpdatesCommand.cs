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
///     Lists the knowledge pages for which a blueprint update brought a newer Blueprint version, through the MCP tool
///     <c>knowledge_list_platform_updates</c> with the signed-in user's token. Changes nothing.
/// </summary>
internal class ListKnowledgePlatformUpdatesCommand : ServiceClientOctoCommand<IAssetServicesClient>
{
    private readonly IOptions<OctoToolAuthenticationOptions> _authenticationOptions;
    private readonly IConsoleService _consoleService;
    private readonly IArgument _jsonArg;
    private readonly IMcpToolClient _mcpToolClient;
    private readonly IArgument _tenantIdArg;
    private readonly ITenantTokenProvider? _tenantTokenProvider;

    public ListKnowledgePlatformUpdatesCommand(ILogger<ListKnowledgePlatformUpdatesCommand> logger,
        IOptions<OctoToolOptions> options, IAssetServicesClient assetServicesClient,
        IAuthenticationService authenticationService, IOptions<OctoToolAuthenticationOptions> authenticationOptions,
        IMcpToolClient mcpToolClient, IConsoleService consoleService, ITenantTokenProvider? tenantTokenProvider = null)
        : base(logger, Constants.AssetRepositoryServicesGroup, "ListKnowledgePlatformUpdates", // literal for the command-reference generator
            "Lists the knowledge pages with a newer Blueprint version: Adoptable (never edited in the tenant) or Available " +
            "(edited, keeps its content). Calls the MCP service with your token; changes nothing.",
            options, assetServicesClient, authenticationService)
    {
        _authenticationOptions = authenticationOptions;
        _mcpToolClient = mcpToolClient;
        _consoleService = consoleService;
        _tenantTokenProvider = tenantTokenProvider;

        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId",
            ["Id of the tenant of the knowledge base. Default: the tenant of the context"], false, 1);
        _jsonArg = CommandArgumentValue.AddArgument("json", "json",
            ["Print the answer of the MCP tool as JSON instead of a table"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_tenantIdArg, "ai-sandbox")],
                    description: "Show which knowledge pages a blueprint update brought a newer version for")
            ],
            Notes:
            [
                "The AI service adopts unedited pages by itself every 15 minutes; this command shows what is pending now. " +
                "AdoptKnowledgePlatformUpdates adopts the Adoptable pages at once.",
                "Adoptable: the page was never edited in the tenant (its current version is a Blueprint version). Available: " +
                "the page was edited in the tenant and keeps its content; a person restores the newer version or dismisses it.",
                "Calls the MCP tool knowledge_list_platform_updates on <MCP service URI>/<tenant>/mcp with the token of the " +
                "context (exchanged for another tenant like every command). The URI comes only from the context (AddContext -msu or " +
                "Config -msu): there is no per-call override, so your token goes only to the MCP service of the context."
            ]);

    public override async Task Execute()
    {
        var tenantId = KnowledgePlatformUpdatesCommandSupport.TenantId(CommandArgumentValue, _tenantIdArg, Options.Value);
        var endpoint = KnowledgePlatformUpdatesMcp.Endpoint(Options.Value, tenantId);
        var token = await KnowledgePlatformUpdatesMcp.TokenForTenantAsync(_authenticationOptions.Value.AccessToken, tenantId,
            _tenantTokenProvider);

        Logger.LogInformation("Listing knowledge platform updates of tenant '{TenantId}' at {Endpoint}", tenantId, endpoint);
        var answer = KnowledgePlatformUpdatesMcp.RequireSuccess(
            await _mcpToolClient.CallToolAsync(endpoint, token, KnowledgePlatformUpdatesMcp.ListTool,
                new JsonObject { ["tenantId"] = tenantId }), KnowledgePlatformUpdatesMcp.ListTool);

        if (CommandArgumentValue.IsArgumentUsed(_jsonArg))
        {
            _consoleService.WriteLine(answer.ToJsonString());
            return;
        }

        Print(_consoleService, tenantId, answer);
    }

    /// <summary>Prints the updates of a <c>knowledge_list_platform_updates</c> answer, one line per page.</summary>
    internal static void Print(IConsoleService console, string tenantId, JsonObject answer)
    {
        var updates = KnowledgePlatformUpdatesMcp.Items(answer, "updates").ToList();
        var adoptable = updates.Count(u => KnowledgePlatformUpdatesMcp.Text(u, "state") == "Adoptable");
        console.WriteLine($"Tenant '{tenantId}': {adoptable} unedited page(s) to adopt, {updates.Count - adoptable} edited " +
                          "page(s) with a newer Blueprint version.");
        foreach (var update in updates)
        {
            var dismissed = KnowledgePlatformUpdatesMcp.Number(update, "dismissedVersion");
            console.WriteLine($"  {KnowledgePlatformUpdatesMcp.Text(update, "state"),-10} {KnowledgePlatformUpdatesMcp.Text(update, "slug")}  " +
                              $"v{KnowledgePlatformUpdatesMcp.Number(update, "currentVersion")} -> " +
                              $"v{KnowledgePlatformUpdatesMcp.Number(update, "newerVersion")}  " +
                              $"{KnowledgePlatformUpdatesMcp.Text(update, "blueprint")}" +
                              (dismissed > 0 ? $"  (dismissed v{dismissed})" : ""));
        }

        if (KnowledgePlatformUpdatesMcp.Flag(answer, "truncated"))
        {
            console.WriteLine("Not every Blueprint version was read (safety cap of the MCP service); run again after adopting.");
        }
    }
}

/// <summary>Argument handling shared by the knowledge platform-update commands.</summary>
internal static class KnowledgePlatformUpdatesCommandSupport
{
    /// <summary>The tenant from <c>-tid</c> or the context, lower case.</summary>
    public static string TenantId(ICommandArgumentValue arguments, IArgument tenantIdArg, OctoToolOptions options)
    {
        var tenantId = arguments.IsArgumentUsed(tenantIdArg)
            ? arguments.GetArgumentScalarValue<string>(tenantIdArg)
            : options.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw ToolException.NoTenantIdConfigured();
        }

        return tenantId.Trim().ToLowerInvariant();
    }
}
