using System.Collections.Immutable;
using Beutl.Engine.SourceGenerators.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceGeneratorTest;

[TestFixture]
public sealed class PropertyReferenceComparisonAnalyzerTests
{
    private const string Stubs = """
        namespace Beutl
        {
            public interface IProperty { }
            public interface IProperty<T> : IProperty { T Value { get; set; } }
            public sealed class ValueProperty<T> : IProperty<T> { public T Value { get; set; } = default!; }
        }
        """;

    private static readonly CSharpCompilation s_baseCompilation = CSharpCompilation.Create(
        "AnalyzerTest",
        [CSharpSyntaxTree.ParseText(Stubs)],
        CompilationReferences.Framework,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    [TestCase("a == b", "==")]
    [TestCase("a != b", "!=")]
    [TestCase("(object)a == c", "==")]
    [TestCase("a.Equals(b)", "Equals")]
    [TestCase("c.Equals((object)a)", "Equals")]
    [TestCase("Equals(a, b)", "Equals")]
    [TestCase("object.Equals(a, c)", "Equals")]
    [TestCase("(IProperty<int>)o == b", "==")]
    [TestCase("(IProperty)o != a", "!=")]
    [TestCase("((IProperty<int>)o).Equals(b)", "Equals")]
    [TestCase("object.Equals((IProperty)o, a)", "Equals")]
    public void TwoPropertyObjects_AreReported(string comparison, string compared)
    {
        Diagnostic diagnostic = Analyze(comparison).Single();
        Assert.That(diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan), Is.EqualTo(comparison));
        Assert.That(diagnostic.GetMessage(), Does.StartWith($"'{compared}' compares two property objects by reference"));
    }

    [TestCase("a == null")]
    [TestCase("null != a")]
    [TestCase("a == default")]
    [TestCase("a == (IProperty)null")]
    [TestCase("a == default(IProperty<int>)")]
    [TestCase("ReferenceEquals(a, b)")]
    [TestCase("a.Value == b.Value")]
    [TestCase("a.Value.Equals(b.Value)")]
    [TestCase("a == o")]
    [TestCase("Equals(a, o)")]
    public void OtherComparisons_AreNotReported(string comparison)
    {
        Assert.That(Analyze(comparison), Is.Empty);
    }

    private static ImmutableArray<Diagnostic> Analyze(string comparison)
    {
        string source = $$"""
            using Beutl;
            public sealed class Example
            {
                public bool Compare(IProperty<int> a, IProperty<int> b, ValueProperty<int> c, object o) => {{comparison}};
            }
            """;
        CSharpCompilation compilation = s_baseCompilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(source));
        Assert.That(
            compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error),
            Is.Empty,
            "the test source must compile, or the assertions below prove nothing");

        return [.. compilation
            .WithAnalyzers([new PropertyReferenceComparisonAnalyzer()])
            .GetAnalyzerDiagnosticsAsync()
            .GetAwaiter()
            .GetResult()
            .Where(static d => d.Id == "BESG007")];
    }
}
