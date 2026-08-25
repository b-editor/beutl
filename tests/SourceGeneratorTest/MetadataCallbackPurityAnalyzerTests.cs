using System.Collections.Immutable;
using Beutl.Engine.SourceGenerators.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceGeneratorTest;

/// <summary>
/// Pins that a render metadata callback which is not a stable, state-free delegate is reported where it is
/// written.
/// </summary>
/// <remarks>
/// The engine used to walk the delegate's closure while recording to catch this. Recording is the render
/// path and does no reflection now, so this rule is the whole of what enforces it, and it has to hold on
/// both sides: a stable, state-free delegate must stay silent, or authors will suppress it.
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

                public static RenderBoundsContract Create<TState>(
                    TState state,
                    Func<TState, Rect, Rect> transformBounds,
                    Func<TState, Rect, Rect> getRequiredInputBounds) => default;
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

    /// <remarks>
    /// The delegate's target is a box the conversion allocates, so the same method group produces a
    /// reference-unequal delegate every time it is written. The plan key is the delegate, so this shape keys
    /// each frame differently and no compiled plan is ever reused.
    /// </remarks>
    [Test]
    public void AMethodGroupOnAReadonlyStruct_IsReported()
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
            Does.Contain("BESG003"),
            "boxing the receiver makes a fresh delegate each conversion, so the plan key never repeats");
    }

    /// <remarks>
    /// A method group carries no closure, but an instance method's receiver becomes the delegate's target.
    /// On a reference type that target is the author's own object, so changing a field on it changes what
    /// the callback answers while its identity stays the method.
    /// </remarks>
    [Test]
    public void AMethodGroupOnAReferenceType_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class Provider
            {
                public float Inset { get; set; }

                public Rect Map(Rect value) => value;
            }

            internal static class Author
            {
                public static RenderBoundsContract Build(Provider provider)
                    => RenderBoundsContract.Create(provider.Map, static value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Contain("BESG003"));
    }

    /// <remarks>
    /// This is the shape the diagnostic tells authors to move to, so it has to be accepted: the callbacks are
    /// static and cached, and the values they read arrive as call state the contract does not key on.
    /// </remarks>
    [Test]
    public void AStaticLambdaOnTheStatePassingOverload_IsNotReported()
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
                    => RenderBoundsContract.Create(
                        new Metadata(inset),
                        static (state, value) => state.Map(value),
                        static (state, value) => state.Map(value));
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Not.Contain("BESG003"));
    }

    [Test]
    public void AStaticMethodGroup_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Author
            {
                private static Rect Map(Rect value) => value;

                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(Map, static value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Not.Contain("BESG003"));
    }

    /// <remarks>
    /// A forwarded callback is checked nowhere else: the caller hands it to the helper, not to a contract,
    /// so the caller's own call is not a contract call and is not analyzed. The forwarder is the last place
    /// that knows a contract is involved.
    /// </remarks>
    [Test]
    public void AForwardedParameter_IsReported()
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
            Does.Contain("BESG003"),
            "nothing else sees that a contract is involved, so this is the last place to say so");
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

    /// <remarks>
    /// A cast is still a delegate argument, and the delegate underneath it is the ordinary capturing one the
    /// runtime validator accepts. Classifying the expression as written rather than the delegate underneath
    /// let this shape through silently.
    /// </remarks>
    [Test]
    public void AForwardedParameterBehindACast_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Author
            {
                public static RenderBoundsContract Build(Func<Rect, Rect> map)
                    => RenderBoundsContract.Create((Func<Rect, Rect>)map, static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG003"),
            "a cast does not change which delegate is handed to the contract");
    }

    [Test]
    public void AForwardedParameterInParentheses_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Author
            {
                public static RenderBoundsContract Build(Func<Rect, Rect> map)
                    => RenderBoundsContract.Create((map), static value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Contain("BESG003"));
    }

    [Test]
    public void AForwardedParameterBehindANullSuppression_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Author
            {
                public static RenderBoundsContract Build(Func<Rect, Rect>? map)
                    => RenderBoundsContract.Create(map!, static value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Contain("BESG003"));
    }

    [Test]
    public void ACapturingLambdaBehindACast_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Author
            {
                public static RenderBoundsContract Build(Rect size)
                    => RenderBoundsContract.Create((Func<Rect, Rect>)(_ => size), static value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Contain("BESG003"));
    }

    /// <remarks>
    /// Unwrapping must not turn the rule into a blanket reject: the shape the diagnostic recommends still has
    /// to survive being written behind a cast.
    /// </remarks>
    [Test]
    public void AStaticLambdaBehindACast_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        (Func<Rect, Rect>)(static value => value),
                        ((static value => value)));
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Not.Contain("BESG003"));
    }

    /// <remarks>
    /// An expression the rule cannot classify is reported rather than waved through: silence has to mean the
    /// rule looked at the delegate, not that it ran out of cases.
    /// </remarks>
    [Test]
    public void AnUnrecognisedDelegateExpression_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Author
            {
                private static Func<Rect, Rect> Factory() => static value => value;

                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(Factory(), static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG003"),
            "the rule cannot see what the factory returns, so it must not assume the delegate is stable");
    }

    /// <remarks>
    /// A null callback carries no state and no identity, so reporting it would be the rule complaining about
    /// something other than purity. The contract rejects it at run time on its own.
    /// </remarks>
    [Test]
    public void ANullCallback_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(null!, default(Func<Rect, Rect>)!);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Not.Contain("BESG003"));
    }

    /// <remarks>
    /// A static lambda cannot reach a local, a parameter, or this, so BESG003 is right to stay silent - but
    /// static state is still reachable, and changing it makes one structural identity stand for two answers.
    /// </remarks>
    [Test]
    public void AStaticLambdaReadingAMutableStaticField_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(value.X + Settings.Offset, value.Y, value.Width, value.Height),
                        static value => value);
            }
            """);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Select(static d => d.Id), Does.Contain("BESG004"));
            Assert.That(
                diagnostics.Select(static d => d.Id),
                Does.Not.Contain("BESG003"),
                "the lambda is static, so the capture rule is right to stay silent; this is a separate failure");
        });
    }

    [Test]
    public void AStaticLambdaReadingASettableStaticProperty_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset { get; set; }
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(value.X + Settings.Offset, value.Y, value.Width, value.Height),
                        static value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Contain("BESG004"));
    }

    /// <remarks>
    /// Static state that cannot be reassigned answers the same way on every frame, so the rule must not reject
    /// it. Without this, BESG004 would push authors to suppress it instead of reading it.
    /// </remarks>
    [Test]
    public void AStaticLambdaReadingImmutableStaticState_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public const float Inset = 1f;

                public static readonly float Margin = 2f;

                public static float Padding { get; } = 3f;
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            value.X + Settings.Inset + Settings.Margin + Settings.Padding,
                            value.Y,
                            value.Width,
                            value.Height),
                        static value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// The getter is where a get-only property either proves itself or does not. This one reads a field
    /// anything can assign, so the property answers differently on the next frame while the delegate the
    /// plan is keyed by stays the same - the exact failure BESG004 exists to name.
    /// </remarks>
    [Test]
    public void AStaticLambdaReadingAGetOnlyPropertyOverMutableState_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeCallbackReading(
            "public static float CurrentOffset;\n\n    public static float Offset => CurrentOffset;",
            "value.X + Settings.Offset");

        Assert.Multiple(() =>
        {
            Assert.That(
                diagnostics.Select(static d => d.Id),
                Does.Contain("BESG004"),
                "having no setter says nothing about what the getter reads");
            Assert.That(
                diagnostics.Select(static d => d.Id),
                Does.Not.Contain("BESG003"),
                "the lambda is static, so the capture rule is right to stay silent");
        });
    }

    [Test]
    public void AStaticLambdaReadingAGetOnlyPropertyThatCallsAMethod_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeCallbackReading(
            "public static float Compute() => 1f;\n\n    public static float Offset => Compute();",
            "value.X + Settings.Offset");

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "what the called method reads is exactly what this rule is documented not to see");
    }

    /// <remarks>
    /// A property compiled into a referenced assembly carries no getter this rule can read, and
    /// <see cref="Environment.TickCount"/> is what that hides: get-only, and a different value every read.
    /// Not being able to look is the reporting side, or the rule would wave through every framework
    /// property an author happens to name.
    /// </remarks>
    [Test]
    public void AStaticLambdaReadingAGetOnlyPropertyWithNoSource_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeCallbackReading(
            "public static float Unused;",
            "value.X + System.Environment.TickCount");

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "a getter with no source proves nothing, so it must not be assumed constant");
    }

    /// <remarks>
    /// These are what stop BESG004 becoming a blanket reject on get-only properties, which authors would
    /// suppress wholesale and lose the rule with it. Each getter can answer only one value, so the plan a
    /// callback reading it compiles to stays correct.
    /// </remarks>
    [TestCase("public static float Value => 4f;", "value.X + Settings.Value")]
    [TestCase("public const float Inset = 1f;\n\n    public static float Value => Inset;", "value.X + Settings.Value")]
    [TestCase("public static Alignment Value => Alignment.Center;", "value.X + (float)Settings.Value")]
    [TestCase("private static readonly float s_margin = 2f;\n\n    public static float Value => s_margin;", "value.X + Settings.Value")]
    [TestCase("public static float Value { get { return 5f; } }", "value.X + Settings.Value")]
    [TestCase("public static float Value { get; } = 6f;", "value.X + Settings.Value")]
    [TestCase("public static float Value => default;", "value.X + Settings.Value")]
    public void AStaticLambdaReadingAProvenConstantGetter_IsNotReported(string members, string read)
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeCallbackReading(members, read);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    private static ImmutableArray<Diagnostic> AnalyzeCallbackReading(string members, string read)
        => Analyze($$"""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal enum Alignment { Start, Center }

            internal static class Settings
            {
                {{members}}
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect({{read}}, value.Y, value.Width, value.Height),
                        static value => value);
            }
            """);

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

        // A source that does not bind produces no analyzer diagnostics, which would let a "stays accepted"
        // case pass without the analyzer ever having looked at it.
        Assert.That(
            compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error),
            Is.Empty,
            "the test source must compile, or the assertions below prove nothing");

        return compilation
            .WithAnalyzers([new MetadataCallbackPurityAnalyzer()])
            .GetAnalyzerDiagnosticsAsync()
            .GetAwaiter()
            .GetResult();
    }
}
