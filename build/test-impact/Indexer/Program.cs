using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// This is deliberately a syntax *superset*, not a claimed semantic call graph. Matching every
// callable name includes overloads, interface dispatch and method groups. Python
// walks the reverse graph and unions it with execution evidence; unsupported changes fail open.
var input = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(args[0]))!;
var trees = input.ToDictionary(p => p.Key,
    p => CSharpSyntaxTree.ParseText(p.Value, new CSharpParseOptions(LanguageVersion.Preview)));
var globalAliases = trees.Values.SelectMany(t => t.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>())
    .Where(u => u.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword) && u.Alias != null).ToArray();
var files = new Dictionary<string, object>();
foreach (var (path, source) in input)
{
    var tree = trees[path];
    var root = tree.GetRoot();
    var declarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
    var methods = new List<object>();
    foreach (var node in declarations)
    {
        var types = node.Ancestors().OfType<TypeDeclarationSyntax>().Reverse().ToArray();
        var ns = string.Join('.', node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse().Select(n => n.Name.ToString()));
        var typeName = string.Join('+', types.Select(t => t.Identifier.ValueText
            + (t.TypeParameterList is { } p ? "`" + p.Parameters.Count : "")));
        var className = string.IsNullOrEmpty(ns) ? typeName : ns + "." + typeName;
        var signature = Tokens(node.WithBody(null).WithExpressionBody(null));
        var key = path + "::" + className + "." + node.Identifier.ValueText + ":" + Hash(signature);
        var attributes = node.AttributeLists.SelectMany(x => x.Attributes)
            .Select(a => a.Name.ToString().Split('.').Last().Replace("Attribute", "")).ToArray();
        var references = References(node, root, globalAliases);
        var span = tree.GetLineSpan(node.Span);
        bool opaque = IsOpaque(node, references);
        methods.Add(new
        {
            key,
            name = node.Identifier.ValueText,
            className,
            hash = Hash(Tokens(node)),
            signature = Hash(signature),
            start = span.StartLinePosition.Line + 1,
            end = span.EndLinePosition.Line + 1,
            references,
            opaque,
            staticClass = node.Modifiers.Any(SyntaxKind.StaticKeyword)
                && types.LastOrDefault()?.Modifiers.Any(SyntaxKind.StaticKeyword) == true
                && types.All(t => t.TypeParameterList == null)
                && !node.ParameterList.Parameters.Any(p => p.Modifiers.Any(SyntaxKind.ThisKeyword))
                ? types.Last().Identifier.ValueText : null,
            lifecycle = attributes.Any(a => a is "SetUp" or "TearDown" or "OneTimeSetUp" or "OneTimeTearDown"),
            test = attributes.Any(a => a is "Test" or "TestCase" or "TestCaseSource" or "Theory" or "AvaloniaTest"),
        });
    }

    // Removing whole ordinary methods lets additions be analyzed through their callers. Changes
    // to signatures are checked separately. Fields, constructors, accessors, attributes, base
    // types, usings and all other syntax remain here and therefore trigger a full fallback.
    var skeleton = root.RemoveNodes(declarations, SyntaxRemoveOptions.KeepNoTrivia) ?? root;
    files[path] = new
    {
        hash = Hash(source),
        skeleton = Hash(Tokens(skeleton)),
        methods,
        parseError = tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error),
        conditional = root.DescendantTrivia(descendIntoTrivia: true).Any(t => t.IsDirective),
        opaque = IsOpaque(root, References(root, root, globalAliases)),
        references = References(skeleton, root, globalAliases),
    };
}
File.WriteAllText(args[1], JsonSerializer.Serialize(files));

static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
static string Tokens(SyntaxNode node) => string.Join(' ', node.DescendantTokens().Select(t => t.Text));
static bool IsOpaque(SyntaxNode node, string[] references) =>
    references.Select(r => r.Split('|').Last()).Any(x => x is "GetMethod" or "GetMethods" or "Invoke"
        or "CreateInstance" or "GetProperty" or "GetProperties" or "GetField" or "GetFields" or "GetType"
        or "GetConstructor" or "GetMembers" or "GetCustomAttribute" or "GetCustomAttributes" or "LoadFrom")
    || node.DescendantTokens().Any(t => t.ValueText is "dynamic" or "DllImport" or "LibraryImport"
        or "TestCaseSource" or "TestFixtureSource")
    || node.DescendantNodes().OfType<FunctionPointerTypeSyntax>().Any();

static string[] References(SyntaxNode node, SyntaxNode root, UsingDirectiveSyntax[] globalAliases)
{
    var result = new HashSet<string>();
    var aliases = root.DescendantNodes().OfType<UsingDirectiveSyntax>().Concat(globalAliases)
        .Where(u => u.Alias != null && u.Name != null)
        .GroupBy(u => u.Alias!.Name.Identifier.ValueText)
        .ToDictionary(g => g.Key, g => g.Select(u => u.Name!.ToString().Split('.').Last()).ToArray());
    foreach (var name in node.DescendantNodes().OfType<SimpleNameSyntax>())
    {
        if (name.Parent is MemberAccessExpressionSyntax access && access.Name == name)
        {
            var receiver = access.Expression.ToString().Replace("global::", "").Split('.').Last();
            result.Add(receiver + "|" + name.Identifier.ValueText);
            if (aliases.TryGetValue(receiver, out var targets))
                foreach (var target in targets)
                    result.Add(target + "|" + name.Identifier.ValueText);
        }
        else if (name.Parent is MemberBindingExpressionSyntax)
            result.Add("?|" + name.Identifier.ValueText);
        else if (name.Parent is InvocationExpressionSyntax invocation && invocation.Expression == name
            || name.Parent is ArgumentSyntax
            || name.Parent is EqualsValueClauseSyntax
            || name.Parent is ReturnStatementSyntax
            || name.Parent is ArrowExpressionClauseSyntax
            || name.Parent is AssignmentExpressionSyntax assignment && assignment.Right == name
            || name.Parent is ExpressionElementSyntax or InitializerExpressionSyntax
                or ConditionalExpressionSyntax or SwitchExpressionArmSyntax or CastExpressionSyntax or BinaryExpressionSyntax
            || name.Parent is LambdaExpressionSyntax)
            result.Add(name.Identifier.ValueText);
    }
    foreach (var token in node.DescendantTokens().Where(t => t.IsKind(SyntaxKind.StringLiteralToken)))
        if (SyntaxFacts.IsValidIdentifier(token.ValueText))
            result.Add(token.ValueText);
    return result.ToArray();
}
