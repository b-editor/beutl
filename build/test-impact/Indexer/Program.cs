using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// This is deliberately a syntax *superset*, not a claimed semantic call graph. Matching every
// identifier of the same name includes overloads, interface dispatch and method groups. Python
// walks the reverse graph and unions it with execution evidence; unsupported changes fail open.
var input = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(args[0]))!;
var files = new Dictionary<string, object>();
foreach (var (path, source) in input)
{
    var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
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
        var references = References(node);
        var span = tree.GetLineSpan(node.Span);
        bool opaque = references.Any(x => x is "dynamic" or "GetMethod" or "GetMethods" or "Invoke"
            or "CreateInstance" or "GetProperty" or "GetField" or "GetType" or "LoadFrom"
            or "DllImport" or "LibraryImport" or "TestCaseSource" or "TestFixtureSource")
            || node.DescendantNodes().OfType<IdentifierNameSyntax>().Any(n => n.Identifier.ValueText == "dynamic");
        methods.Add(new
        {
            key, name = node.Identifier.ValueText, className,
            hash = Hash(Tokens(node)), signature = Hash(signature),
            start = span.StartLinePosition.Line + 1, end = span.EndLinePosition.Line + 1,
            references, opaque,
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
        hash = Hash(source), skeleton = Hash(Tokens(skeleton)), methods,
        parseError = tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error),
        conditional = root.DescendantTrivia(descendIntoTrivia: true).Any(t => t.IsDirective),
        references = References(skeleton),
    };
}
File.WriteAllText(args[1], JsonSerializer.Serialize(files));

static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
static string Tokens(SyntaxNode node) => string.Join(' ', node.DescendantTokens().Select(t => t.Text));
static string[] References(SyntaxNode node) => node.DescendantNodes().OfType<SimpleNameSyntax>()
    .Select(n => n.Identifier.ValueText)
    .Concat(node.DescendantTokens().Where(t => t.IsKind(SyntaxKind.StringLiteralToken)).Select(t => t.ValueText))
    .Distinct().ToArray();
