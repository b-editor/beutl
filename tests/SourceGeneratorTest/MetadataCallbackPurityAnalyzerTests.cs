using System.Collections.Immutable;
using Beutl.Engine.SourceGenerators.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceGeneratorTest;

/// <summary>
/// Pins that a render metadata callback which can read changing state is reported where it is written.
/// </summary>
/// <remarks>
/// The engine used to walk the delegate's closure while recording to catch this. Recording is the render
/// path and does no reflection now, so this rule is the whole of what enforces it, and it has to hold on
/// both sides: the shapes that are pure must stay silent, or authors will suppress it.
/// </remarks>
[TestFixture]
public sealed class MetadataCallbackPurityAnalyzerTests
{
    private const string ContractStubs = """
        namespace Beutl.Graphics
        {
            public readonly record struct Rect(float X, float Y, float Width, float Height);
        }

        namespace Beutl.Graphics.Rendering
        {
            using System;
            using Beutl.Graphics;

            public readonly struct RenderBoundsContract
            {
                public static RenderBoundsContract Create(
                    Func<Rect, Rect> transformBounds,
                    Func<Rect, Rect> getRequiredInputBounds) => default;
            }
        }

        namespace Beutl.Graphics.Effects
        {
            using System;

            public sealed class ShaderDefinitionBuilder<TState>
            {
                public void Uniform<T>(string name, Func<TState, T> value) { }
            }
        }
        """;

    [Test]
    public void ACapturingLambda_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Author
            {
                public static RenderBoundsContract Build(Rect size)
                    => RenderBoundsContract.Create(_ => size, static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG003"),
            "a lambda that reads a value the caller supplies per call is the case this rule exists for");
    }

    [Test]
    public void AStaticLambda_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(static value => value, static value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Not.Contain("BESG003"));
    }

    [Test]
    public void AMethodGroupOnAReadonlyStruct_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal readonly record struct Metadata(float Inset)
            {
                public Rect Map(Rect value) => value;
            }

            internal static class Author
            {
                public static RenderBoundsContract Build(float inset)
                    => RenderBoundsContract.Create(new Metadata(inset).Map, static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Not.Contain("BESG003"),
            "the delegate's target is a copy of the struct, which nothing can reach afterwards");
    }

    [Test]
    public void AForwardedParameter_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Author
            {
                public static RenderBoundsContract Build(Func<Rect, Rect> map)
                    => RenderBoundsContract.Create(map, static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Not.Contain("BESG003"),
            "the caller's own call site is where the rule applies to the caller's callback");
    }

    /// <remarks>
    /// The generic builder is what an out-of-tree shader author writes against, and its deferred binder is
    /// keyed the same way a bounds map is, so the rule has to reach it through the type arguments rather
    /// than past them.
    /// </remarks>
    [Test]
    public void ACapturingShaderBinder_OnTheGenericBuilder_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics.Effects;

            internal static class Author
            {
                public static void Bind(ShaderDefinitionBuilder<float> builder, float multiplier)
                    => builder.Uniform("amount", state => state * multiplier);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG003"),
            "a binder that reads a value the caller supplies per call is the case this rule exists for");
    }

    private static ImmutableArray<Diagnostic> Analyze(string source)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "AnalyzerTest",
            [
                CSharpSyntaxTree.ParseText(ContractStubs),
                CSharpSyntaxTree.ParseText(source),
            ],
            AppDomain.CurrentDomain.GetAssemblies()
                .Where(static a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(static a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
                .ToArray(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation
            .WithAnalyzers([new MetadataCallbackPurityAnalyzer()])
            .GetAnalyzerDiagnosticsAsync()
            .GetAwaiter()
            .GetResult();
    }
}
