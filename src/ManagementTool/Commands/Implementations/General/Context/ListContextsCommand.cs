using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.General.Context;

internal class ListContextsCommand : Command<OctoToolOptions>
{
    private readonly IContextManager _contextManager;
    private readonly IConsoleService _consoleService;
    private readonly IArgument _nameArg;
    private readonly IArgument _jsonArg;

    public ListContextsCommand(ILogger<ListContextsCommand> logger, IOptions<OctoToolOptions> options,
        IContextManager contextManager, IConsoleService consoleService)
        : base(logger, Constants.ContextGroup, "ListContexts",
            "Lists all configured contexts. Pass -n to show details for a single context.", options)
    {
        _contextManager = contextManager;
        _consoleService = consoleService;

        _nameArg = CommandArgumentValue.AddArgument("n", "name",
            ["Name of the context to show details for. Omit to list all contexts."], 1);
        _jsonArg = CommandArgumentValue.AddArgument("j", "json", ["Output as raw JSON"], false);
    }

    public override Task Execute()
    {
        var contexts = _contextManager.ListContexts();
        var activeName = _contextManager.GetActiveContextName();
        var isJson = CommandArgumentValue.IsArgumentUsed(_jsonArg);

        if (CommandArgumentValue.IsArgumentUsed(_nameArg))
        {
            var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg);
            if (!contexts.TryGetValue(name, out var entry))
            {
                Logger.LogError("Context '{Name}' does not exist", name);
                return Task.CompletedTask;
            }

            if (isJson)
            {
                _consoleService.WriteLine(BuildJson(new[] { (name, entry) }, activeName));
            }
            else
            {
                WriteDetail(name, entry, activeName == name);
            }

            return Task.CompletedTask;
        }

        if (contexts.Count == 0)
        {
            if (isJson)
            {
                _consoleService.WriteLine("[]");
            }
            else
            {
                Logger.LogInformation("No contexts configured. Use 'AddContext' to create one.");
            }

            return Task.CompletedTask;
        }

        if (isJson)
        {
            _consoleService.WriteLine(BuildJson(contexts.Select(kv => (kv.Key, kv.Value)), activeName));
        }
        else
        {
            WriteTable(contexts, activeName);
        }

        return Task.CompletedTask;
    }

    private void WriteTable(IReadOnlyDictionary<string, ContextEntry> contexts, string? activeName)
    {
        _consoleService.WriteLine("");
        _consoleService.WriteLine("Available contexts:");
        _consoleService.WriteLine("==========================================");

        foreach (var (name, entry) in contexts)
        {
            var marker = name == activeName ? " *" : "";
            var tenant = entry.OctoToolOptions.TenantId ?? "(not set)";
            var identity = entry.OctoToolOptions.IdentityServiceUrl ?? "(not set)";
            var auth = DescribeAuth(entry.Authentication);
            _consoleService.WriteLine($"  {name}{marker}");
            _consoleService.WriteLine($"    Identity: {identity}");
            _consoleService.WriteLine($"    Tenant:   {tenant}");
            _consoleService.WriteLine($"    Auth:     {auth}");
        }

        _consoleService.WriteLine("");
        _consoleService.WriteLine("* = active context");
    }

    private void WriteDetail(string name, ContextEntry entry, bool isActive)
    {
        var o = entry.OctoToolOptions;
        _consoleService.WriteLine("");
        _consoleService.WriteLine($"Context: {name}{(isActive ? " (active)" : "")}");
        _consoleService.WriteLine("==========================================");
        _consoleService.WriteLine($"  Tenant:                 {o.TenantId ?? "(not set)"}");
        _consoleService.WriteLine($"  Identity Service:       {o.IdentityServiceUrl ?? "(not set)"}");
        _consoleService.WriteLine($"  Asset Service:          {o.AssetServiceUrl ?? "(not set)"}");
        _consoleService.WriteLine($"  Bot Service:            {o.BotServiceUrl ?? "(not set)"}");
        _consoleService.WriteLine($"  Communication Service:  {o.CommunicationServiceUrl ?? "(not set)"}");
        _consoleService.WriteLine($"  Reporting Service:      {o.ReportingServiceUrl ?? "(not set)"}");
        _consoleService.WriteLine($"  AI Service:             {o.AiServiceUrl ?? "(not set)"}");
        _consoleService.WriteLine($"  Admin Panel:            {o.AdminPanelUrl ?? "(not set)"}");
        _consoleService.WriteLine($"  Auth:                   {DescribeAuth(entry.Authentication)}");
    }

    private static string DescribeAuth(OctoToolAuthenticationOptions auth)
    {
        if (string.IsNullOrEmpty(auth.AccessToken))
        {
            return "no token (run LogIn)";
        }

        if (auth.AccessTokenExpiresAt == null)
        {
            return "authenticated (no expiry recorded)";
        }

        var expiresAtUtc = auth.AccessTokenExpiresAt.Value.ToUniversalTime();
        var expired = expiresAtUtc <= DateTime.UtcNow;
        var status = expired ? "expired" : "authenticated";
        var refresh = string.IsNullOrEmpty(auth.RefreshToken) ? "" : ", refresh available";
        return $"{status} (expires {expiresAtUtc:O}{refresh})";
    }

    private static string BuildJson(IEnumerable<(string Name, ContextEntry Entry)> contexts, string? activeName)
    {
        var list = contexts.Select(c =>
        {
            var auth = c.Entry.Authentication;
            string authStatus;
            if (string.IsNullOrEmpty(auth.AccessToken))
            {
                authStatus = "none";
            }
            else if (auth.AccessTokenExpiresAt == null)
            {
                authStatus = "unknown-expiry";
            }
            else if (auth.AccessTokenExpiresAt.Value.ToUniversalTime() <= DateTime.UtcNow)
            {
                authStatus = "expired";
            }
            else
            {
                authStatus = "valid";
            }

            return new
            {
                name = c.Name,
                isActive = c.Name == activeName,
                tenant = c.Entry.OctoToolOptions.TenantId,
                services = new
                {
                    identity = c.Entry.OctoToolOptions.IdentityServiceUrl,
                    asset = c.Entry.OctoToolOptions.AssetServiceUrl,
                    bot = c.Entry.OctoToolOptions.BotServiceUrl,
                    communication = c.Entry.OctoToolOptions.CommunicationServiceUrl,
                    reporting = c.Entry.OctoToolOptions.ReportingServiceUrl,
                    ai = c.Entry.OctoToolOptions.AiServiceUrl,
                    adminPanel = c.Entry.OctoToolOptions.AdminPanelUrl
                },
                authStatus,
                accessTokenExpiresAt = auth.AccessTokenExpiresAt,
                hasRefreshToken = !string.IsNullOrEmpty(auth.RefreshToken)
            };
        });

        return JsonConvert.SerializeObject(list, Formatting.Indented);
    }
}
