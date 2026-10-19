using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.GraphQl;

/// <summary>
///     Sends one GraphQL query or mutation to a tenant and writes the response JSON, unmodified, to standard output
///     (AB#7027). The token is the one for the addressed tenant: the SDK exchanges the token of the context for a
///     token of that tenant (AB#6836) and never falls back to the context token, so scripts no longer read a token
///     out of the context file.
///     <para>
///         Standard output carries the answer and nothing else (the log goes to standard error, see
///         <see cref="MachineReadableOutput" />). GraphQL errors end the command with exit code -5 and the error body
///         on standard error.
///     </para>
/// </summary>
internal class GraphQlCommand : ServiceClientOctoCommand<IAssetServicesClient>
{
    public const string CommandName = "GraphQl";

    private readonly IArgument _fileArg;
    private readonly IArgument _operationNameArg;
    private readonly IArgument _queryArg;
    private readonly IArgument _stdinArg;
    private readonly IStandardStreams _streams;
    private readonly IArgument _tenantIdArg;
    private readonly IArgument _variablesArg;

    public GraphQlCommand(ILogger<GraphQlCommand> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IAuthenticationService authenticationService,
        IStandardStreams streams)
        : base(logger, Constants.AssetRepositoryServicesGroup, "GraphQl", // literal on purpose: the command-reference generator needs a string literal here
            "Runs a GraphQL query or mutation against a tenant and prints the response JSON to standard output. " +
            "Authenticates with a token for that tenant.",
            options, assetServicesClient, authenticationService)
    {
        _streams = streams;

        _tenantIdArg = CommandArgumentValue.AddArgument("tid", "tenantId",
            ["Id of the tenant to query. Default: the tenant of the context"], false, 1);
        _queryArg = CommandArgumentValue.AddArgument("q", "query", ["The GraphQL query or mutation text"], false, 1);
        _fileArg = CommandArgumentValue.AddArgument("f", "file",
            ["File with the GraphQL document"], false, 1);
        _stdinArg = CommandArgumentValue.AddArgument("si", "stdin",
            ["Read the GraphQL document from standard input (also the default when input is piped and no document is given)"],
            false, 0);
        _variablesArg = CommandArgumentValue.AddArgument("vars", "variables",
            ["Variables as a JSON object, or '@<file>' to read the JSON object from a file"], false, 1);
        _operationNameArg = CommandArgumentValue.AddArgument("on", "operationName",
            ["Name of the operation to run when the document holds several"], false, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(
                    arguments:
                    [
                        new CodeSampleArgument(_tenantIdArg, "meshtest"),
                        new CodeSampleArgument(_queryArg, "{ runtime { systemTenants { totalCount } } }")
                    ],
                    description: "Query a tenant; the response JSON is the only thing written to standard output",
                    expectedOutput: """{"data":{"runtime":{"systemTenants":{"totalCount":3}}}}"""),
                new CodeSample(
                    arguments:
                    [
                        new CodeSampleArgument(_tenantIdArg, "meshtest"),
                        new CodeSampleArgument(_fileArg, "query.graphql"),
                        new CodeSampleArgument(_variablesArg, "{\"first\":10}")
                    ],
                    description: "Query from a file with variables"),
                new CodeSample(
                    arguments: [new CodeSampleArgument(_tenantIdArg, "meshtest"), new CodeSampleArgument(_stdinArg)],
                    description: "Query from standard input, e.g. `cat query.graphql | octo-cli -c GraphQl -tid meshtest -si`")
            ]
        );

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

        var query = await ReadQueryAsync();
        var variables = await ReadVariablesAsync();
        var operationName = CommandArgumentValue.IsArgumentUsed(_operationNameArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_operationNameArg)
            : null;

        GraphQlRawResponse response;
        try
        {
            response = await ServiceClient.SendGraphQlAsync(tenantId, query, variables, operationName);
        }
        catch (ArgumentException e) when (e.ParamName == "variablesJson")
        {
            throw new ToolException(e.Message);
        }

        if (response.HasErrors)
        {
            _streams.WriteError(response.Content);
            throw new ToolException(
                $"The GraphQL request to tenant '{tenantId}' returned errors (HTTP {(int)response.StatusCode}); " +
                "the response body is on standard error.");
        }

        _streams.WriteOutput(response.Content);
    }

    private async Task<string> ReadQueryAsync()
    {
        var hasQuery = CommandArgumentValue.IsArgumentUsed(_queryArg);
        var hasFile = CommandArgumentValue.IsArgumentUsed(_fileArg);
        var hasStdin = CommandArgumentValue.IsArgumentUsed(_stdinArg);
        if ((hasQuery ? 1 : 0) + (hasFile ? 1 : 0) + (hasStdin ? 1 : 0) > 1)
        {
            throw new ToolException("Use only one of --query, --file and --stdin.");
        }

        string text;
        if (hasQuery)
        {
            text = CommandArgumentValue.GetArgumentScalarValue<string>(_queryArg);
        }
        else if (hasFile)
        {
            text = await ReadFileAsync(CommandArgumentValue.GetArgumentScalarValue<string>(_fileArg));
        }
        else if (hasStdin || _streams.IsInputRedirected)
        {
            text = await ReadStandardInputAsync();
        }
        else
        {
            throw new ToolException(
                "No GraphQL document given. Use --query <text>, --file <path> or pipe the document to standard input.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ToolException("The GraphQL document is empty.");
        }

        return text;
    }

    private async Task<string?> ReadVariablesAsync()
    {
        if (!CommandArgumentValue.IsArgumentUsed(_variablesArg))
        {
            return null;
        }

        var value = CommandArgumentValue.GetArgumentScalarValue<string>(_variablesArg);
        return value.StartsWith('@') ? await ReadFileAsync(value[1..]) : value;
    }

    private async Task<string> ReadStandardInputAsync()
    {
        if (!_streams.IsInputRedirected)
        {
            throw new ToolException("Standard input is a terminal; pipe the GraphQL document into the command.");
        }

        return await _streams.ReadInputToEndAsync();
    }

    private static async Task<string> ReadFileAsync(string path)
    {
        if (!File.Exists(path))
        {
            throw new ToolException($"File '{path}' does not exist.");
        }

        return await File.ReadAllTextAsync(path);
    }
}
