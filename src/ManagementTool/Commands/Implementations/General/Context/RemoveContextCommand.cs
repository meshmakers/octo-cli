using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.General.Context;

internal class RemoveContextCommand : Command<OctoToolOptions>
{
    private readonly IContextManager _contextManager;
    private readonly IArgument _nameArg;

    public RemoveContextCommand(ILogger<RemoveContextCommand> logger, IOptions<OctoToolOptions> options,
        IContextManager contextManager)
        : base(logger, Constants.ContextGroup, "RemoveContext", "Removes a named context.", options)
    {
        _contextManager = contextManager;

        _nameArg = CommandArgumentValue.AddArgument("n", "name",
            ["Name of the context to remove"], true, 1);
    }

    public override Task Execute()
    {
        var name = CommandArgumentValue.GetArgumentScalarValue<string>(_nameArg);

        Logger.LogInformation("Removing context '{ContextName}'", name);

        var contexts = _contextManager.ListContexts();
        if (!contexts.ContainsKey(name))
        {
            throw new ToolException($"Context '{name}' does not exist.");
        }

        _contextManager.RemoveContext(name);

        var activeContextName = _contextManager.GetActiveContextName();
        if (activeContextName != null)
        {
            Logger.LogInformation("Context '{ContextName}' removed. Active context: '{ActiveContext}'", name,
                activeContextName);
        }
        else
        {
            Logger.LogInformation("Context '{ContextName}' removed. No active context.", name);
        }

        return Task.CompletedTask;
    }
}
