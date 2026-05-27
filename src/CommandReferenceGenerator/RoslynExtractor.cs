using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meshmakers.Octo.Frontend.CommandReferenceGenerator;

public static class RoslynExtractor
{
    private static readonly Dictionary<string, ArgumentDescriptor[]> InheritedArgsByBaseClass = new()
    {
        ["JobWithWaitOctoCommand"] = new[]
        {
            new ArgumentDescriptor("w", "wait", "Wait for a import job to complete", IsRequired: false, ValueCount: 0)
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

            var args = new List<ArgumentDescriptor>();
            if (ctor.Body is not null)
            {
                foreach (var invocation in ctor.Body.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (!IsAddArgumentCall(invocation)) continue;
                    args.Add(ExtractArgument(invocation, knownConstants));
                }
            }

            var baseClassName = GetBaseClassName(classDecl);
            if (baseClassName != null && InheritedArgsByBaseClass.TryGetValue(baseClassName, out var inheritedArgs))
            {
                args.AddRange(inheritedArgs);
            }

            var sidecar = SidecarLoader.Load(sourceFilePath, classDecl.Identifier.Text);
            commands.Add(new CommandDescriptor(group, verb, description, args)
            {
                ClassName = classDecl.Identifier.Text,
                ExamplesMarkdown = sidecar.Examples,
                NotesMarkdown = sidecar.Notes,
                SeeAlsoMarkdown = sidecar.SeeAlso,
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
