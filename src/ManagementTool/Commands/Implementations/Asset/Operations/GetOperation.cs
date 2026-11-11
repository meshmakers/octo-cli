using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.Methods;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Operations;

/// <summary>
///     AB#8446: shows one operation of an asynchronous CK method (state, progress, result or error) through
///     <c>runtime.operations</c> of the asset repository; <c>-w</c> waits until it is final and prints the progress.
/// </summary>
internal class GetOperation : ServiceClientOctoCommand<ITenantClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _operationIdArg;
    private readonly IArgument _waitArg;
    private readonly IArgument _jsonArg;

    public GetOperation(ILogger<GetOperation> logger, IOptions<OctoToolOptions> options, ITenantClient tenantClient,
        IAuthenticationService authenticationService, IConsoleService consoleService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "GetOperation",
            "Shows an operation of an asynchronous CK method (e.g. a dump): state, progress and the result or error. Read only.",
            options, tenantClient, authenticationService)
    {
        _consoleService = consoleService;
        _operationIdArg = CommandArgumentValue.AddArgument("id", "operationId",
            ["Id of the operation (printed by the command that started it, e.g. Dump)"], true, 1);
        _waitArg = CommandArgumentValue.AddArgument("w", "wait",
            ["Wait until the operation has succeeded or failed and print its progress meanwhile"], false, 0);
        _jsonArg = CommandArgumentValue.AddArgument("j", "json", ["Output the operation as JSON"], false, 0);
    }

    /// <summary>The time provider of the wait (tests).</summary>
    internal TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [new CodeSampleArgument(_operationIdArg, "93c1a391ba834f71815763e7897cc2cf")],
                    description: "Show an operation"),
                new CodeSample(
                    arguments: [new CodeSampleArgument(_operationIdArg, "93c1a391ba834f71815763e7897cc2cf"), new CodeSampleArgument(_waitArg)],
                    description: "Wait until the operation is final, printing its progress")
            ],
            Notes:
            [
                "The tenant is the tenant of the context. The caller who started the operation and tenant administrators can read it.",
                "Operations are kept for a retention period after they ended (7 days by default, configurable on the platform). An unknown, expired or foreign id is reported as OPERATION_NOT_FOUND.",
                "-w ends with exit code -3 when the operation failed. Stopping the command (Ctrl+C) only stops waiting; operations cannot be cancelled.",
                "Standard output carries only the answer (text or -j JSON); banner and log lines go to standard error, so the output can be piped (e.g. to jq)."
            ]);

    public override async Task Execute()
    {
        if (string.IsNullOrWhiteSpace(Options.Value.TenantId))
        {
            throw ToolException.NoTenantIdConfigured();
        }

        var operationId = CommandArgumentValue.GetArgumentScalarValue<string>(_operationIdArg).Trim();
        var operations = new CkOperationsClient(ServiceClient);

        CkOperation operation;
        if (CommandArgumentValue.IsArgumentUsed(_waitArg))
        {
            operation = await operations.WaitForCompletionAsync(new CkOperationHandle(operationId),
                new OperationProgressLogger(Logger, "Operation"),
                new CkOperationWaitOptions
                {
                    WaitBeforeFirstRead = false,
                    TimeProvider = TimeProvider,
                    BeforeReadAsync = _ => EnsureAuthenticatedAsync()
                });
        }
        else
        {
            operation = await operations.GetOperationAsync(operationId);
        }

        if (CommandArgumentValue.IsArgumentUsed(_jsonArg))
        {
            _consoleService.WriteLine(OperationText.Json(operation));
        }
        else
        {
            foreach (var line in OperationText.Details(operation))
            {
                _consoleService.WriteLine(line);
            }
        }

        if (CommandArgumentValue.IsArgumentUsed(_waitArg) && operation.State == CkOperationState.Failed)
        {
            throw new Meshmakers.Octo.Sdk.ServiceClient.ServiceClientException(
                $"Operation '{operation.OperationId}' failed: " +
                (operation.Error != null ? OperationText.Error(operation.Error) : "no error reported"));
        }
    }
}
