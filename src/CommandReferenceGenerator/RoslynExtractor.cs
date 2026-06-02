using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meshmakers.Octo.Frontend.CommandReferenceGenerator;

public static class RoslynExtractor
{
    private static readonly Dictionary<string, (string FieldName, ArgumentDescriptor Descriptor)[]> InheritedArgsByBaseClass = new()
    {
        ["JobWithWaitOctoCommand"] = new[]
        {
            ("_waitForJobArg", new ArgumentDescriptor("w", "wait", "Wait for a import job to complete", IsRequired: false, ValueCount: 0)),
        },
    };

    private static string? GetBaseClassName(ClassDeclarationSyntax classDecl)
    {
        var baseType = classDecl.BaseList?.Types.FirstOrDefault()?.Type;
        return baseType switch
        {
            SimpleNameSyntax simple => simple.Identifier.Text,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            _ => null
        };
    }

    public static IReadOnlyList<CommandDescriptor> Extract(string source)
        => Extract(source, sourceFilePath: "", new Dictionary<string, string>());

    public static IReadOnlyList<CommandDescriptor> Extract(string source,
        IReadOnlyDictionary<string, string> knownConstants)
        => Extract(source, sourceFilePath: "", knownConstants);

    public static IReadOnlyList<CommandDescriptor> Extract(string source,
        string sourceFilePath,
        IReadOnlyDictionary<string, string> knownConstants)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var commands = new List<CommandDescriptor>();

        foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var ctor = classDecl.Members.OfType<ConstructorDeclarationSyntax>().FirstOrDefault();
            if (ctor?.Initializer is null) continue;
            if (ctor.Initializer.ThisOrBaseKeyword.Text != "base") continue;

            var baseArgs = ctor.Initializer.ArgumentList.Arguments;
            if (baseArgs.Count < 3) continue;

            string? group;
            string verb;
            string description;

            var arg1 = baseArgs[1].Expression;
            if (arg1 is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
            {
                // Disambiguate Shape A vs Shape B (with literal group):
                //   Shape A: (logger, "verb", "desc", options, ...) — arg3 is options (NOT literal)
                //   Shape B: (logger, "group", "verb", "desc", options, ...) — arg3 is desc (literal)
                if (baseArgs.Count >= 4 && IsStringLikeExpression(baseArgs[3].Expression))
                {
                    group = lit.Token.ValueText;
                    verb = ExtractStringLiteral(baseArgs[2].Expression, knownConstants);
                    description = ExtractStringLiteral(baseArgs[3].Expression, knownConstants);
                }
                else
                {
                    group = null;
                    verb = lit.Token.ValueText;
                    description = ExtractStringLiteral(baseArgs[2].Expression, knownConstants);
                }
            }
            else if (arg1 is MemberAccessExpressionSyntax member)
            {
                if (baseArgs.Count < 4) continue;
                var groupName = member.Name.Identifier.Text;
                group = knownConstants.TryGetValue(groupName, out var resolved) ? resolved : groupName;
                verb = ExtractStringLiteral(baseArgs[2].Expression, knownConstants);
                description = ExtractStringLiteral(baseArgs[3].Expression, knownConstants);
            }
            else
            {
                continue;
            }

            var (args, argsByField) = ExtractAddArgumentCalls(ctor, knownConstants);

            var baseClassName = GetBaseClassName(classDecl);
            if (baseClassName != null && InheritedArgsByBaseClass.TryGetValue(baseClassName, out var inheritedArgs))
            {
                foreach (var (fieldName, descriptor) in inheritedArgs)
                {
                    args.Add(descriptor);
                    argsByField[fieldName] = descriptor;
                }
            }

            var documentation = ExtractDocumentation(classDecl, argsByField, knownConstants);

            commands.Add(new CommandDescriptor(group, verb, description, args)
            {
                ClassName = classDecl.Identifier.Text,
                Samples = documentation.Samples,
                Notes = documentation.Notes,
            });
        }

        return commands;
    }

    public static IReadOnlyDictionary<string, string> CollectConstants(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var constants = new Dictionary<string, string>();

        foreach (var fieldDecl in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            if (!fieldDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword))) continue;
            if (fieldDecl.Declaration.Type is not PredefinedTypeSyntax pre) continue;
            if (!pre.Keyword.IsKind(SyntaxKind.StringKeyword)) continue;

            foreach (var variable in fieldDecl.Declaration.Variables)
            {
                if (variable.Initializer?.Value is LiteralExpressionSyntax lit
                    && lit.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    constants[variable.Identifier.Text] = lit.Token.ValueText;
                }
            }
        }

        return constants;
    }

    private static bool IsStringLikeExpression(ExpressionSyntax expr) =>
        expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression)
        || expr is BinaryExpressionSyntax bin && bin.IsKind(SyntaxKind.AddExpression)
        || expr is InterpolatedStringExpressionSyntax;

    private static bool IsAddArgumentCall(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax memberAccess
        && memberAccess.Name.Identifier.Text == "AddArgument";

    /// <summary>
    ///     Walks the ctor body for <c>AddArgument(...)</c> calls. Captures the optional
    ///     field-assignment target (e.g. <c>_tenantIdArg = CommandArgumentValue.AddArgument(...)</c>)
    ///     so later sample-extraction can resolve <c>new CodeSampleArgument(_tenantIdArg, ...)</c>
    ///     back to the matching <see cref="ArgumentDescriptor"/>.
    /// </summary>
    private static (List<ArgumentDescriptor> args, Dictionary<string, ArgumentDescriptor> byField) ExtractAddArgumentCalls(
        ConstructorDeclarationSyntax ctor, IReadOnlyDictionary<string, string> knownConstants)
    {
        var args = new List<ArgumentDescriptor>();
        var byField = new Dictionary<string, ArgumentDescriptor>();

        if (ctor.Body is null) return (args, byField);

        foreach (var node in ctor.Body.DescendantNodes())
        {
            if (node is AssignmentExpressionSyntax assign
                && assign.Right is InvocationExpressionSyntax assignInvocation
                && IsAddArgumentCall(assignInvocation))
            {
                var arg = ExtractArgument(assignInvocation, knownConstants);
                args.Add(arg);
                if (assign.Left is IdentifierNameSyntax fieldId)
                    byField[fieldId.Identifier.Text] = arg;
                continue;
            }

            // Standalone (non-assigned) AddArgument — extract for help table but skip field binding.
            if (node is InvocationExpressionSyntax invocation
                && IsAddArgumentCall(invocation)
                && invocation.Parent is not AssignmentExpressionSyntax)
            {
                // Avoid double-counting if walking discovers the same invocation under an AssignmentExpression.
                if (invocation.Ancestors().OfType<AssignmentExpressionSyntax>().Any()) continue;
                args.Add(ExtractArgument(invocation, knownConstants));
            }
        }

        return (args, byField);
    }

    private static ArgumentDescriptor ExtractArgument(InvocationExpressionSyntax invocation,
        IReadOnlyDictionary<string, string>? knownConstants = null)
    {
        var args = invocation.ArgumentList.Arguments;
        var shortName = ExtractStringLiteral(args[0].Expression);
        var longName = ExtractStringLiteral(args[1].Expression);
        var help = ExtractStringArrayLiteral(args[2].Expression, knownConstants);

        bool isRequired;
        int valueCount;

        if (args.Count == 5)
        {
            // (short, long, help, isRequired, valueCount)
            isRequired = ExtractBoolLiteral(args[3].Expression);
            valueCount = ExtractIntLiteral(args[4].Expression);
        }
        else if (args.Count == 6)
        {
            // (short, long, help, isRequired, valueCount, isMultiValue:bool) — 6th arg discarded
            isRequired = ExtractBoolLiteral(args[3].Expression);
            valueCount = ExtractIntLiteral(args[4].Expression);
        }
        else if (args.Count == 4)
        {
            // Two 4-arg overloads exist in real octo-cli:
            //   (short, long, help, isRequired:bool) - flag-only, valueCount defaults to 0
            //   (short, long, help, valueCount:int)  - optional value arg, isRequired defaults to false
            var fourthArg = args[3].Expression;
            if (fourthArg is LiteralExpressionSyntax lit4 &&
                (lit4.IsKind(SyntaxKind.TrueLiteralExpression) || lit4.IsKind(SyntaxKind.FalseLiteralExpression)))
            {
                isRequired = ExtractBoolLiteral(fourthArg);
                valueCount = 0;
            }
            else
            {
                isRequired = false;
                valueCount = ExtractIntLiteral(fourthArg);
            }
        }
        else
        {
            throw new InvalidOperationException($"Unknown AddArgument arity: {args.Count}");
        }

        return new ArgumentDescriptor(shortName, longName, help, isRequired, valueCount);
    }

    // ---- Documentation extraction (GetDocumentation override) ----

    private record struct ExtractedDocumentation(
        IReadOnlyList<SampleDescriptor>? Samples,
        IReadOnlyList<string>? Notes);

    private static ExtractedDocumentation ExtractDocumentation(ClassDeclarationSyntax classDecl,
        IReadOnlyDictionary<string, ArgumentDescriptor> fieldToArg,
        IReadOnlyDictionary<string, string> knownConstants)
    {
        var method = classDecl.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == "GetDocumentation"
                                 && m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.OverrideKeyword)));
        if (method is null) return new(null, null);

        var ctorExpr = FindCommandDocumentationCtor(method);
        if (ctorExpr is null) return new(null, null);

        var ctorArgs = ctorExpr.ArgumentList?.Arguments;
        if (ctorArgs is null) return new(null, null);

        ExpressionSyntax? samplesExpr = null, notesExpr = null;
        for (var i = 0; i < ctorArgs.Value.Count; i++)
        {
            var entry = ctorArgs.Value[i];
            var name = entry.NameColon?.Name.Identifier.Text ?? PositionalParamName(i);
            switch (name)
            {
                case "Samples":
                case "samples": samplesExpr = entry.Expression; break;
                case "Notes":
                case "notes": notesExpr = entry.Expression; break;
            }
        }

        return new(
            samplesExpr is null ? null : ExtractSamples(samplesExpr, fieldToArg, knownConstants),
            notesExpr is null ? null : ExtractStringCollection(notesExpr, knownConstants));
    }

    private static string PositionalParamName(int index) => index switch
    {
        0 => "Samples",
        1 => "Notes",
        _ => string.Empty,
    };

    private static BaseObjectCreationExpressionSyntax? FindCommandDocumentationCtor(MethodDeclarationSyntax method)
    {
        ExpressionSyntax? returnExpr = null;
        if (method.ExpressionBody?.Expression is { } expr) returnExpr = expr;
        else if (method.Body is not null)
        {
            var ret = method.Body.DescendantNodes().OfType<ReturnStatementSyntax>().FirstOrDefault();
            returnExpr = ret?.Expression;
        }
        return returnExpr as BaseObjectCreationExpressionSyntax;
    }

    private static IReadOnlyList<SampleDescriptor>? ExtractSamples(ExpressionSyntax expr,
        IReadOnlyDictionary<string, ArgumentDescriptor> fieldToArg,
        IReadOnlyDictionary<string, string> knownConstants)
    {
        if (expr is not CollectionExpressionSyntax coll) return null;

        var samples = new List<SampleDescriptor>();
        foreach (var element in coll.Elements.OfType<ExpressionElementSyntax>())
        {
            if (element.Expression is not BaseObjectCreationExpressionSyntax ctor) continue;
            var ctorArgs = ctor.ArgumentList?.Arguments;
            if (ctorArgs is null || ctorArgs.Value.Count < 2) continue;

            // Parse positional+named args for CodeSample(arguments, description, expectedOutput?).
            ExpressionSyntax? argumentsExpr = null;
            ExpressionSyntax? descriptionExpr = null;
            ExpressionSyntax? expectedOutputExpr = null;
            for (var i = 0; i < ctorArgs.Value.Count; i++)
            {
                var entry = ctorArgs.Value[i];
                var name = entry.NameColon?.Name.Identifier.Text ?? PositionalSampleParam(i);
                switch (name)
                {
                    case "arguments":
                    case "Arguments": argumentsExpr = entry.Expression; break;
                    case "description":
                    case "Description": descriptionExpr = entry.Expression; break;
                    case "expectedOutput":
                    case "ExpectedOutput": expectedOutputExpr = entry.Expression; break;
                }
            }
            if (argumentsExpr is null || descriptionExpr is null) continue;

            var bindings = ExtractArgumentBindings(argumentsExpr, fieldToArg, knownConstants);
            var desc = ExtractStringLiteral(descriptionExpr, knownConstants);
            string? expectedOutput = expectedOutputExpr is null
                ? null
                : ExtractStringLiteral(expectedOutputExpr, knownConstants);

            samples.Add(new SampleDescriptor(bindings, desc, expectedOutput));
        }
        return samples.Count == 0 ? null : samples;
    }

    private static string PositionalSampleParam(int index) => index switch
    {
        0 => "arguments",
        1 => "description",
        2 => "expectedOutput",
        _ => string.Empty,
    };

    private static IReadOnlyList<SampleArgumentBinding> ExtractArgumentBindings(ExpressionSyntax expr,
        IReadOnlyDictionary<string, ArgumentDescriptor> fieldToArg,
        IReadOnlyDictionary<string, string> knownConstants)
    {
        var bindings = new List<SampleArgumentBinding>();
        if (expr is not CollectionExpressionSyntax coll) return bindings;

        foreach (var element in coll.Elements.OfType<ExpressionElementSyntax>())
        {
            if (element.Expression is not BaseObjectCreationExpressionSyntax ctor) continue;
            var ctorArgs = ctor.ArgumentList?.Arguments;
            if (ctorArgs is null || ctorArgs.Value.Count == 0) continue;

            var fieldExpr = ctorArgs.Value[0].Expression;
            if (fieldExpr is not IdentifierNameSyntax fieldId) continue;
            if (!fieldToArg.TryGetValue(fieldId.Identifier.Text, out var argDesc)) continue;

            string? value = ctorArgs.Value.Count >= 2
                ? ExtractStringLiteral(ctorArgs.Value[1].Expression, knownConstants)
                : null;

            bindings.Add(new SampleArgumentBinding(argDesc, value));
        }
        return bindings;
    }

    private static IReadOnlyList<string>? ExtractStringCollection(ExpressionSyntax expr,
        IReadOnlyDictionary<string, string> knownConstants)
    {
        if (expr is not CollectionExpressionSyntax coll) return null;
        var items = new List<string>();
        foreach (var element in coll.Elements.OfType<ExpressionElementSyntax>())
            items.Add(ExtractStringLiteral(element.Expression, knownConstants));
        return items.Count == 0 ? null : items;
    }

    // ---- String / literal helpers ----

    private static string ExtractStringLiteral(ExpressionSyntax expr,
        IReadOnlyDictionary<string, string>? knownConstants = null)
    {
        if (expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
            return lit.Token.ValueText;

        if (expr is BinaryExpressionSyntax bin && bin.IsKind(SyntaxKind.AddExpression))
            return ExtractStringLiteral(bin.Left, knownConstants) + ExtractStringLiteral(bin.Right, knownConstants);

        if (expr is InterpolatedStringExpressionSyntax interp)
        {
            var sb = new StringBuilder();
            foreach (var part in interp.Contents)
            {
                switch (part)
                {
                    case InterpolatedStringTextSyntax text:
                        sb.Append(text.TextToken.ValueText);
                        break;
                    case InterpolationSyntax interpolation:
                        sb.Append(ResolveInterpolationExpression(interpolation.Expression, knownConstants));
                        break;
                }
            }
            return sb.ToString();
        }

        throw new InvalidOperationException($"Expected string literal, got {expr.Kind()}: {expr}");
    }

    private static string ResolveInterpolationExpression(ExpressionSyntax expr,
        IReadOnlyDictionary<string, string>? constants)
    {
        if (constants != null && expr is MemberAccessExpressionSyntax member)
        {
            var name = member.Name.Identifier.Text;
            if (constants.TryGetValue(name, out var value)) return value;
            return $"{{{name}}}";
        }
        if (constants != null && expr is IdentifierNameSyntax ident)
        {
            var name = ident.Identifier.Text;
            if (constants.TryGetValue(name, out var value)) return value;
            return $"{{{name}}}";
        }
        return $"{{{expr}}}";
    }

    private static bool ExtractBoolLiteral(ExpressionSyntax expr)
    {
        if (expr is LiteralExpressionSyntax lit)
        {
            if (lit.IsKind(SyntaxKind.TrueLiteralExpression)) return true;
            if (lit.IsKind(SyntaxKind.FalseLiteralExpression)) return false;
        }
        throw new InvalidOperationException($"Expected bool literal, got {expr.Kind()}: {expr}");
    }

    private static int ExtractIntLiteral(ExpressionSyntax expr)
    {
        if (expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NumericLiteralExpression))
            return (int)lit.Token.Value!;
        throw new InvalidOperationException($"Expected int literal, got {expr.Kind()}: {expr}");
    }

    private static string ExtractStringArrayLiteral(ExpressionSyntax expr,
        IReadOnlyDictionary<string, string>? knownConstants = null)
    {
        var literals = new List<string>();
        if (expr is CollectionExpressionSyntax coll)
        {
            foreach (var element in coll.Elements.OfType<ExpressionElementSyntax>())
                literals.Add(ExtractStringLiteral(element.Expression, knownConstants));
        }
        else
        {
            throw new InvalidOperationException($"Expected collection expression, got {expr.Kind()}: {expr}");
        }
        return string.Join("\n", literals);
    }
}
