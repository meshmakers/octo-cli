using Meshmakers.Common.Shared.Services;

namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

/// <summary>
///     Hands the parser the command line with the context argument moved to the front, see
///     <see cref="ContextArgumentHoister" />. The pre-scan in <see cref="ContextOverrideResolver" />
///     reads the raw line and is position independent, so both stay consistent.
/// </summary>
public sealed class ContextHoistingEnvironmentService : IEnvironmentService
{
    private readonly IEnvironmentService _inner;

    public ContextHoistingEnvironmentService(IEnvironmentService inner)
    {
        _inner = inner;
    }

    /// <inheritdoc />
    public string[] GetCommandLineArgs()
    {
        return ContextArgumentHoister.Hoist(_inner.GetCommandLineArgs());
    }
}
