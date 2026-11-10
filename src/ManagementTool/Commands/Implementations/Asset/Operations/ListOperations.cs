using System.Globalization;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.Methods;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Operations;

/// <summary>
///     AB#8446: lists operations of asynchronous CK methods, newest first, through <c>runtime.operations</c>: the
///     caller's own, or with <c>-a</c> every caller's (tenant administrators).
/// </summary>
internal class ListOperations : ServiceClientOctoCommand<ITenantClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _methodIdArg;
    private readonly IArgument _stateArg;
    private readonly IArgument _sinceArg;
    private readonly IArgument _allOwnersArg;
    private readonly IArgument _skipArg;
    private readonly IArgument _takeArg;
    private readonly IArgument _jsonArg;

    public ListOperations(ILogger<ListOperations> logger, IOptions<OctoToolOptions> options, ITenantClient tenantClient,
        IAuthenticationService authenticationService, IConsoleService consoleService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "ListOperations",
            "Lists operations of asynchronous CK methods (e.g. dumps), newest first: the caller's own, or every caller's with --allOwners. Read only.",
            options, tenantClient, authenticationService)
    {
        _consoleService = consoleService;
        _methodIdArg = CommandArgumentValue.AddArgument("m", "methodId",
            ["Only operations of this method, e.g. 'System.Bot/TenantRepository.Dump-1'"], false, 1);
        _stateArg = CommandArgumentValue.AddArgument("s", "state",
            ["Only operations in this state: Accepted, Running, Succeeded or Failed"], false, 1);
        _sinceArg = CommandArgumentValue.AddArgument("since", "since",
            ["Only operations accepted at or after this time (ISO 8601, UTC if no offset is given)"], false, 1);
        _allOwnersArg = CommandArgumentValue.AddArgument("a", "allOwners",
            ["List the operations of every caller of the tenant (tenant administrators only)"], false, 0);
        _skipArg = CommandArgumentValue.AddArgument("sk", "skip", ["Number of operations to skip (default 0)"], false, 1);
        _takeArg = CommandArgumentValue.AddArgument("t", "take", ["Page size, 1 to 200 (default 50)"], false, 1);
        _jsonArg = CommandArgumentValue.AddArgument("j", "json", ["Output the page as JSON"], false, 0);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [], description: "List my operations"),
                new CodeSample(
                    arguments:
                    [
                        new CodeSampleArgument(_methodIdArg, "System.Bot/TenantRepository.Dump-1"),
                        new CodeSampleArgument(_stateArg, "Failed")
                    ],
                    description: "List my failed dumps"),
                new CodeSample(arguments: [new CodeSampleArgument(_allOwnersArg)],
                    description: "List the operations of every caller (tenant administrators)")
            ],
            Notes:
            [
                "The tenant is the tenant of the context. Operations are kept 7 days after they ended.",
                "--allOwners without administrator rights is refused with OPERATION_QUERY_REFUSED."
            ]);

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            throw ToolException.NoTenantIdConfigured();
        }

        var filter = new CkOperationListFilter
        {
            MethodId = CommandArgumentValue.IsArgumentUsed(_methodIdArg)
                ? CommandArgumentValue.GetArgumentScalarValue<string>(_methodIdArg)
                : null,
            State = CommandArgumentValue.IsArgumentUsed(_stateArg)
                ? ParseState(CommandArgumentValue.GetArgumentScalarValue<string>(_stateArg))
                : null,
            Since = CommandArgumentValue.IsArgumentUsed(_sinceArg)
                ? ParseSince(CommandArgumentValue.GetArgumentScalarValue<string>(_sinceArg))
                : null,
            AllOwners = CommandArgumentValue.IsArgumentUsed(_allOwnersArg),
            Skip = CommandArgumentValue.IsArgumentUsed(_skipArg)
                ? ParseCount(_skipArg, CommandArgumentValue.GetArgumentScalarValue<string>(_skipArg), 0, int.MaxValue)
                : null,
            Take = CommandArgumentValue.IsArgumentUsed(_takeArg)
                ? ParseCount(_takeArg, CommandArgumentValue.GetArgumentScalarValue<string>(_takeArg), 1, 200)
                : null
        };

        var page = await new CkOperationsClient(ServiceClient).ListOperationsAsync(filter);

        if (CommandArgumentValue.IsArgumentUsed(_jsonArg))
        {
            _consoleService.WriteLine(OperationText.Json(page));
            return;
        }

        _consoleService.WriteLine(
            $"{page.Items.Count.ToString(CultureInfo.InvariantCulture)} of {page.TotalCount.ToString(CultureInfo.InvariantCulture)} operation(s)");
        foreach (var operation in page.Items)
        {
            _consoleService.WriteLine(OperationText.Row(operation));
        }
    }

    internal static CkOperationState ParseState(string value)
    {
        var normalized = value.Trim().Replace("_", string.Empty, StringComparison.Ordinal);
        if (Enum.TryParse<CkOperationState>(normalized, true, out var state) && state != CkOperationState.Unknown &&
            Enum.IsDefined(state))
        {
            return state;
        }

        throw new ToolException($"Unknown state '{value}'. Use Accepted, Running, Succeeded or Failed.");
    }

    internal static DateTime ParseSince(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var since)
            ? since.UtcDateTime
            : throw new ToolException($"'{value}' is not a valid time for --since (ISO 8601, e.g. 2026-11-10T08:00:00Z).");

    private static int ParseCount(IArgument argument, string value, int min, int max) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count >= min && count <= max
            ? count
            : throw new ToolException($"--{argument.LongTerm} must be a number from {min} to {max}.");
}
