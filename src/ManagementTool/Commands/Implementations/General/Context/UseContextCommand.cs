using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.General.Context;

internal class UseContextCommand : Command<OctoToolOptions>
{
    private readonly IContextManager _contextManager;
    private readonly IConsoleService _consoleService;
    private readonly IArgument _nameArg;

    public UseContextCommand(ILogger<UseContextCommand> logger, IOptions<OctoToolOptions> options,
        IContextManager contextManager, IConsoleService consoleService)
        : base(logger, Constants.ContextGroup, "UseContext", "Switches the active context.", options)
    {
        _contextManager = contextManager;
        _consoleService = consoleService;

        _nameArg = CommandArgumentValue.AddArgument("n", "name",
            ["Name of the context to activate. Omit to list all contexts."], 1);
    }

    public override Task Execute()
    {
        if (!CommandArgumentValue.IsArgumentUsed(_nameArg))
        {
            // List all contexts
            var contexts = _contextManager.ListContexts();
            var activeContextName = _contextManager.GetActiveContextName();

            if (contexts.Count == 0)
            {
                Logger.LogInformation("No contexts configured. Use 'AddContext' to create one.");
                return Task.CompletedTask;
            }

            _consoleService.WriteLine("");
            _consoleService.WriteLine("Available contexts:");
            _consoleService.WriteLine("==========================================");

            foreach (var (name, entry) in contexts)
            {
                var marker = name == activeContextName ? " *" : "";
                var tenant = entry.OctoToolOptions.TenantId ?? "(not set)";
                var identity = entry.OctoToolOptions.IdentityServiceUrl ?? "(not set)";
                _consoleService.WriteLine($"  {name}{marker}");
                _consoleService.WriteLine($"    Identity: {identity}");
                _consoleService.WriteLine($"    Tenant:   {tenant}");
            }

            _consoleService.WriteLine("");
            _consoleService.WriteLine("* = active context");
            return Task.CompletedTask;
        }

        var contextName = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg);

        _contextManager.SetActiveContext(contextName);

        Logger.LogInformation("Switched to context '{ContextName}'", contextName);

        return Task.CompletedTask;
    }
}
