using System.IO.Compression;
using System.Text;
using System.Text.Json;
using GraphQL;
using GraphQL.SystemTextJson;
using GraphQL.Types;
using GraphQL.Utilities;
using GraphQL.Validation;
using GraphQLParser;
using GraphQLParser.AST;

namespace ManagementTool.Tests;

/// <summary>
///     The GraphQL schema of a real asset repository, to validate the GraphQL documents octo-cli sends (AB#8588). The
///     schema is an introspection snapshot (descriptions stripped, gzip) of tenant <c>ai-sandbox</c> on test-2 in
///     <c>Fixtures/asset-repo-schema.test-2-ai-sandbox.json.gz</c>; it contains the System models only. The validator is
///     GraphQL.NET, the library and version the asset repository uses, with its standard rules (field and argument names,
///     variable types in their positions, input values and enum values of the variables).
///     <para>
///         Mocked HTTP responses never reach this validation, so a document can pass every unit test and still be
///         refused by the server (SetKnowledgePageStatus declared <c>$slug: String!</c> for a <c>SimpleScalar</c>).
///     </para>
///     <para>
///         Refresh the snapshot when a command uses a type the snapshot does not have (read-only introspection with
///         <c>Fixtures/asset-repo-introspection.graphql</c>, any user context of a tenant with the needed models), see
///         the octo-cli CLAUDE.md section "GraphQL documents against the asset repository schema".
///     </para>
/// </summary>
internal static class AssetRepositoryGraphQlSchema
{
    private const string SnapshotFile = "asset-repo-schema.test-2-ai-sandbox.json.gz";

    /// <summary>
    ///     AB#8446: snapshot of a test-2 scratch tenant with System.Bot 3.7.0 and an asset repository with operation
    ///     handles (AB#8444): <c>systemBotTenantRepositorys.dump</c> and <c>runtime.operations</c>, which the ai-sandbox
    ///     snapshot predates. Same recording steps; System models only.
    /// </summary>
    public const string SystemBotSnapshotFile = "asset-repo-schema.test-2-system-bot-3.7.json.gz";

    private static readonly Lazy<ISchema> LazySystemBotSchema = new(() => Build(SystemBotSnapshotFile));

    private static readonly Lazy<ISchema> LazySchema = new(() => Build(SnapshotFile));

    /// <summary>
    ///     AB#8968: the ai-sandbox snapshot predates System.Ai 4.11.0, whose <c>KnowledgePageVersion</c> has the attribute
    ///     <c>BaseVersion</c> (AB#8780). This overlay adds exactly that input field (<c>baseVersion: Int</c>, the GraphQL
    ///     name of an optional CK Int attribute) to the snapshot until it is refreshed from a 4.11.0 tenant.
    /// </summary>
    private static readonly Lazy<ISchema> LazySystemAi411Schema = new(() => Build(SnapshotFile,
        new Dictionary<string, string> { ["SystemAiKnowledgePageVersionInput"] = "baseVersion: Int" }));

    public static ISchema Schema => LazySchema.Value;

    /// <summary>
    ///     Validates <paramref name="document" /> and, when given, its <paramref name="variablesJson" /> against the
    ///     schema. Returns the validation messages; empty when the server would accept the request.
    /// </summary>
    public static Task<IReadOnlyList<string>> ValidateAsync(string document, string? variablesJson = null) =>
        ValidateAsync(Schema, document, variablesJson);

    /// <summary>
    ///     AB#8446: validates against the System.Bot 3.7.0 snapshot (<see cref="SystemBotSnapshotFile" />).
    /// </summary>
    public static Task<IReadOnlyList<string>> ValidateAgainstSystemBotAsync(string document, string? variablesJson = null) =>
        ValidateAsync(LazySystemBotSchema.Value, document, variablesJson);

    /// <summary>AB#8968: validates against the ai-sandbox snapshot with the System.Ai 4.11.0 overlay (<c>baseVersion</c>).</summary>
    public static Task<IReadOnlyList<string>> ValidateAgainstSystemAi411Async(string document, string? variablesJson = null) =>
        ValidateAsync(LazySystemAi411Schema.Value, document, variablesJson);

    private static async Task<IReadOnlyList<string>> ValidateAsync(ISchema schema, string document, string? variablesJson)
    {
        var parsed = Parser.Parse(document);
        var operation = parsed.Definitions.OfType<GraphQLOperationDefinition>().Single();
        var variables = variablesJson == null
            ? Inputs.Empty
            : new GraphQLSerializer().Deserialize<Inputs>(variablesJson) ?? Inputs.Empty;

        var result = await new DocumentValidator().ValidateAsync(new ValidationOptions
        {
            Schema = schema,
            Document = parsed,
            Operation = operation,
            Rules = DocumentValidator.CoreRules,
            Variables = variables,
            RequestServices = null,
            User = null
        });
        return result.Errors.Select(e => e.Message).ToList();
    }

    private static ISchema Build(string snapshotFile, IReadOnlyDictionary<string, string>? extraInputFields = null)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", snapshotFile);
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gzip);
        var schemaElement = doc.RootElement.GetProperty("data").GetProperty("__schema");

        var sdl = ToSdl(schemaElement, out var customScalars, extraInputFields);
        var schema = GraphQL.Types.Schema.For<ScalarRegisteringSchemaBuilder>(sdl,
            builder => builder.CustomScalars = customScalars);
        schema.Initialize();
        return schema;
    }

    private static readonly HashSet<string> SpecScalars = ["String", "Int", "Float", "Boolean", "ID"];

    /// <summary>Turns an introspection result into SDL (types only; descriptions and directives are not needed).</summary>
    private static string ToSdl(JsonElement schema, out List<string> customScalars,
        IReadOnlyDictionary<string, string>? extraInputFields = null)
    {
        customScalars = [];
        var sdl = new StringBuilder();
        sdl.Append("schema { query: ").Append(schema.GetProperty("queryType").GetProperty("name").GetString());
        if (schema.GetProperty("mutationType") is { ValueKind: JsonValueKind.Object } mutation)
        {
            sdl.Append(" mutation: ").Append(mutation.GetProperty("name").GetString());
        }

        sdl.AppendLine(" }");

        foreach (var type in schema.GetProperty("types").EnumerateArray())
        {
            var name = type.GetProperty("name").GetString()!;
            if (name.StartsWith("__", StringComparison.Ordinal))
            {
                continue;
            }

            switch (type.GetProperty("kind").GetString())
            {
                case "SCALAR":
                    if (!SpecScalars.Contains(name))
                    {
                        customScalars.Add(name);
                        sdl.Append("scalar ").AppendLine(name);
                    }

                    break;
                case "ENUM":
                    sdl.Append("enum ").Append(name).Append(" { ")
                        .AppendJoin(' ', type.GetProperty("enumValues").EnumerateArray()
                            .Select(v => v.GetProperty("name").GetString()))
                        .AppendLine(" }");
                    break;
                case "INPUT_OBJECT":
                    sdl.Append("input ").Append(name).AppendLine(" {");
                    foreach (var field in type.GetProperty("inputFields").EnumerateArray())
                    {
                        sdl.Append("  ").AppendLine(InputValue(field));
                    }

                    if (extraInputFields != null && extraInputFields.TryGetValue(name, out var extra))
                    {
                        sdl.Append("  ").AppendLine(extra);
                    }

                    sdl.AppendLine("}");
                    break;
                case "UNION":
                    sdl.Append("union ").Append(name).Append(" = ")
                        .AppendJoin(" | ", type.GetProperty("possibleTypes").EnumerateArray()
                            .Select(t => t.GetProperty("name").GetString()))
                        .AppendLine();
                    break;
                case "OBJECT":
                case "INTERFACE":
                    sdl.Append(type.GetProperty("kind").GetString() == "OBJECT" ? "type " : "interface ").Append(name);
                    if (type.GetProperty("interfaces") is { ValueKind: JsonValueKind.Array } interfaces &&
                        interfaces.GetArrayLength() > 0)
                    {
                        sdl.Append(" implements ").AppendJoin(" & ",
                            interfaces.EnumerateArray().Select(t => t.GetProperty("name").GetString()));
                    }

                    sdl.AppendLine(" {");
                    foreach (var field in type.GetProperty("fields").EnumerateArray())
                    {
                        sdl.Append("  ").Append(field.GetProperty("name").GetString());
                        var args = field.GetProperty("args");
                        if (args.GetArrayLength() > 0)
                        {
                            sdl.Append('(').AppendJoin(", ", args.EnumerateArray().Select(InputValue)).Append(')');
                        }

                        sdl.Append(": ").AppendLine(TypeRef(field.GetProperty("type")));
                    }

                    sdl.AppendLine("}");
                    break;
            }
        }

        return sdl.ToString();
    }

    private static string InputValue(JsonElement value)
    {
        var text = $"{value.GetProperty("name").GetString()}: {TypeRef(value.GetProperty("type"))}";
        return value.TryGetProperty("defaultValue", out var defaultValue) && defaultValue.ValueKind == JsonValueKind.String
            ? $"{text} = {defaultValue.GetString()}"
            : text;
    }

    private static string TypeRef(JsonElement type) => type.GetProperty("kind").GetString() switch
    {
        "NON_NULL" => TypeRef(type.GetProperty("ofType")) + "!",
        "LIST" => "[" + TypeRef(type.GetProperty("ofType")) + "]",
        _ => type.GetProperty("name").GetString()!
    };

    /// <summary>
    ///     Registers the asset repository's own scalars (SimpleScalar, OctoObjectId, …) before the SDL is read. This test
    ///     checks positions and shapes, not their value formats, so they accept any value.
    /// </summary>
    private sealed class ScalarRegisteringSchemaBuilder : SchemaBuilder
    {
        public IReadOnlyList<string> CustomScalars { get; set; } = [];

        protected override void PreConfigure(GraphQL.Types.Schema schema)
        {
            base.PreConfigure(schema);
            foreach (var name in CustomScalars)
            {
                schema.RegisterType(new AnyValueScalarGraphType(name));
            }
        }

        // Nothing is executed: abstract types need no real type resolution, only one for the schema check.
        protected override UnionGraphType ToUnionType(GraphQLUnionTypeDefinition unionDef)
        {
            var type = base.ToUnionType(unionDef);
            type.ResolveType ??= _ => null;
            return type;
        }

        protected override InterfaceGraphType ToInterfaceType(GraphQLInterfaceTypeDefinition interfaceDef)
        {
            var type = base.ToInterfaceType(interfaceDef);
            type.ResolveType ??= _ => null;
            return type;
        }
    }

    private sealed class AnyValueScalarGraphType : ScalarGraphType
    {
        public AnyValueScalarGraphType(string name)
        {
            Name = name;
        }

        public override object? ParseLiteral(GraphQLValue value) => value switch
        {
            GraphQLNullValue => null,
            GraphQLStringValue s => (string)s.Value,
            GraphQLIntValue i => (string)i.Value,
            GraphQLFloatValue f => (string)f.Value,
            GraphQLBooleanValue b => b.BoolValue,
            GraphQLEnumValue e => (string)e.Name.Value,
            _ => value
        };

        public override object? ParseValue(object? value) => value;

        public override object? Serialize(object? value) => value;
    }
}
