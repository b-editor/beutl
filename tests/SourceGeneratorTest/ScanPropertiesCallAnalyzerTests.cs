using System.Collections.Immutable;
using Beutl.Engine.SourceGenerators.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceGeneratorTest;

[TestFixture]
public sealed class ScanPropertiesCallAnalyzerTests
{
    private const string Stubs = """
        global using Beutl;

        namespace Beutl
        {
            public interface IProperty { }
            public interface IProperty<T> : IProperty { }
            public interface IListProperty<T> : IProperty<System.Collections.Generic.IList<T>> { }

            public class CoreObject
            {
                protected void ScanProperties<T>() where T : CoreObject { }
            }
        }

        namespace Beutl.Engine
        {
            public static class Property
            {
                public static IProperty<T> Create<T>() => null!;
                public static IListProperty<T> CreateList<T>() => null!;
            }

            public class EngineObject : CoreObject
            {
            }
        }
        """;

    private static readonly CSharpCompilation s_baseCompilation = CSharpCompilation.Create(
        "AnalyzerTest",
        [CSharpSyntaxTree.ParseText(Stubs)],
        CompilationReferences.Framework,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    [Test]
    public void TypeWithoutConstructor_IsReportedOnItsName()
    {
        string source = """
            using Beutl.Engine;
            public sealed class Example : EngineObject
            {
                public IProperty<int> Value { get; } = Property.Create<int>();
            }
            """;
        Diagnostic diagnostic = Analyze(source).Single();
        Assert.That(diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan), Is.EqualTo("Example"));
        Assert.That(diagnostic.GetMessage(), Does.StartWith("'Example' declares properties"));
    }

    [TestCase("ScanProperties<Example>();", 0)]
    [TestCase("", 1)]
    [TestCase("ScanProperties<Base>();", 1)]
    public void EachConstructorMustScanItsOwnType(string otherConstructorBody, int expected)
    {
        string source = $$"""
            using Beutl.Engine;
            public class Base : EngineObject
            {
                public Base() => ScanProperties<Base>();
                public IProperty<int> First { get; } = Property.Create<int>();
            }
            public class Example : Base
            {
                public Example() { ScanProperties<Example>(); }
                public Example(int value) { {{otherConstructorBody}} }
                public IListProperty<int> Items { get; } = Property.CreateList<int>();
            }
            """;
        Assert.That(Analyze(source), Has.Length.EqualTo(expected));
    }

    [TestCase("ScanProperties<Example>();", 0)]
    [TestCase("", 1)]
    public void ChainedConstructor_IsCheckedThroughItsTarget(string targetBody, int expected)
    {
        string source = $$"""
            using Beutl.Engine;
            public abstract class Example : EngineObject
            {
                protected Example() : this(0) { }
                protected Example(int value) { {{targetBody}} }
                public IProperty<int> Value { get; } = Property.Create<int>();
            }
            """;
        Assert.That(Analyze(source), Has.Length.EqualTo(expected));
    }

    [Test]
    public void GenericType_ScanningItselfWithItsTypeParameters_IsAccepted()
    {
        string source = """
            using Beutl.Engine;
            public class Box<T> : EngineObject
            {
                public Box() => ScanProperties<Box<T>>();
                public IProperty<T> Value { get; } = Property.Create<T>();
            }
            """;
        Assert.That(Analyze(source), Is.Empty);
    }

    [TestCase("ScanProperties<Settings>();", 0)]
    [TestCase("", 1)]
    public void ACoreObjectThatIsNotAnEngineObject_IsCheckedToo(string body, int expected)
    {
        string source = $$"""
            using Beutl.Engine;
            public sealed class Settings : CoreObject
            {
                public Settings() { {{body}} }
                public IProperty<int> Value { get; } = Property.Create<int>();
            }
            """;
        Assert.That(Analyze(source), Has.Length.EqualTo(expected));
    }

    [Test]
    public void TypesWithNothingToScan_AreNotReported()
    {
        string source = """
            using Beutl.Engine;
            public class Base : EngineObject
            {
                public Base() => ScanProperties<Base>();
                public IProperty<int> Value { get; } = Property.Create<int>();
            }
            public sealed class InheritsOnly : Base
            {
                internal IProperty<int> Hidden { get; } = Property.Create<int>();
                public static IProperty<int> Shared { get; } = Property.Create<int>();
                public int Plain { get; set; }
            }
            public sealed class NotAnEngineObject
            {
                public IProperty<int> Value { get; } = Property.Create<int>();
            }
            """;
        Assert.That(Analyze(source), Is.Empty);
    }

    private static ImmutableArray<Diagnostic> Analyze(string source)
    {
        CSharpCompilation compilation = s_baseCompilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(source));
        Assert.That(
            compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error),
            Is.Empty,
            "the test source must compile, or the assertions below prove nothing");

        return [.. compilation
            .WithAnalyzers([new ScanPropertiesCallAnalyzer()])
            .GetAnalyzerDiagnosticsAsync()
            .GetAwaiter()
            .GetResult()
            .Where(static d => d.Id == "BESG006")];
    }
}
