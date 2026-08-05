using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.Frontend.ManagementTool.Services;

namespace Meshmakers.Octo.Frontend.ManagementTool;

/// <summary>
///     The command parser plus octo-cli's own top-level arguments — currently the context selector.
///     Declaring it here keeps <c>Meshmakers.Common.CommandLineParser</c> free of tool specifics
///     while still making the argument known to validation and to the usage output.
/// </summary>
internal class OctoCommandParser : CommandParser
{
    private readonly IArgument _contextArg;
    private readonly IParserService _parserService;
    private readonly ContextSelection _contextSelection;

    public OctoCommandParser(IParserService parserService, IEnumerable<ICommand> commands,
        ContextSelection contextSelection)
        : base(parserService, commands)
    {
        _parserService = parserService;
        _contextSelection = contextSelection;

        // Short and long term are identical: no abbreviation is wanted, but AddArgument requires a
        // non-empty short term. The side effect is that the usage line reads
        // "--context (-context)", and that only the full term is accepted — see
        // ContextOverrideResolver for why that matters.
        _contextArg = parserService.AddArgument(Constants.ContextArgumentTerm, Constants.ContextArgumentTerm,
        [
            "Name of the context to use for this invocation only, instead of the active one",
            $"Does not change the active context. Also settable via {Constants.EnvVarContext}"
        ], 1);
    }

    /// <inheritdoc />
    public override async Task ParseAndValidateAsync(string? applicationExeName = null)
    {
        // Parsing twice is cheap and has no side effects (ParseLayer clears its state first). It
        // buys the chance to compare the parser's view of --context against the pre-scan that
        // actually selected the context, before any command runs.
        _parserService.ParseAndValidate();
        VerifyContextSelection();

        await base.ParseAndValidateAsync(applicationExeName);
    }

    /// <summary>
    ///     Fails when the parser saw a context argument the pre-scan did not resolve to the same
    ///     value. Both match the term the same way, so this should be unreachable — but the
    ///     alternative to an error would be running against the wrong context without saying so.
    /// </summary>
    private void VerifyContextSelection()
    {
        if (_parserService.IsHelpRequested || !_parserService.IsArgumentUsed(_contextArg))
        {
            return;
        }

        var parsedName = _parserService.GetArgumentValue(_contextArg).GetValue(string.Empty)?.Trim();
        if (string.Equals(parsedName, _contextSelection.CommandLineName, StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidParameterException(
            $"The context argument could not be applied. Use '--{Constants.ContextArgumentTerm} <name>' " +
            $"or set {Constants.EnvVarContext}.");
    }
}
