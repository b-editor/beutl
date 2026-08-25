using System.Collections.Immutable;
using System.IO;
using Beutl.Engine.SourceGenerators.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;

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
    private static readonly MetadataReference[] FrameworkReferences = AppDomain.CurrentDomain
        .GetAssemblies()
        .Where(static a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
        .Select(static a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
        .ToArray();

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

            public sealed class OpaqueRenderSession { }

            public sealed class TargetScopeSession { }

            public sealed class TargetCommandSession { }

            public sealed class RawTargetScopeSession { }

            public sealed class RawTargetCommandSession { }

            public sealed class OpaqueRenderDefinition<TState>
            {
                public static OpaqueRenderDefinition<TState> Create(
                    Action<OpaqueRenderSession, TState> execute,
                    RenderBoundsContract bounds) => null!;
            }

            public sealed class TargetScopeDefinition<TState>
            {
                public static TargetScopeDefinition<TState> Create(
                    Action<TargetScopeSession, TState> execute,
                    RenderBoundsContract bounds) => null!;
            }

            public sealed class TargetCommandDefinition<TState>
            {
                public static TargetCommandDefinition<TState> Create(
                    Action<TargetCommandSession, TState> execute,
                    RenderBoundsContract bounds) => null!;
            }

            public sealed class RawTargetScopeDefinition<TState>
            {
                public static RawTargetScopeDefinition<TState> Create(
                    Action<RawTargetScopeSession, TState> execute,
                    RenderBoundsContract bounds) => null!;
            }

            public sealed class RawTargetCommandDefinition<TState>
            {
                public static RawTargetCommandDefinition<TState> Create(
                    Action<RawTargetCommandSession, TState> execute,
                    RenderBoundsContract bounds) => null!;
            }
        }

        namespace Beutl.Graphics.Effects
        {
            using System;
            using Beutl.Graphics.Rendering;

            public sealed class ShaderDefinitionBuilder<TState>
            {
                public void Uniform<T>(string name, Func<TState, T> value) { }
            }

            public sealed class GeometrySession { }

            public sealed class GeometryDefinition<TState>
            {
                public static GeometryDefinition<TState> Create(
                    Action<GeometrySession, TState> render,
                    RenderBoundsContract bounds) => null!;
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

    /// <remarks>
    /// readonly fixes the reference the plan key needs and says nothing about the delegate the field holds.
    /// A field assigned in a constructor is where that gap shows: the delegate is built from the
    /// constructor's arguments, and the runtime validator accepts an ordinary closure target.
    /// </remarks>
    [Test]
    public void ACapturingLambdaAssignedToAReadonlyField_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class Author
            {
                private readonly Func<Rect, Rect> _map;

                public Author(Rect size) => _map = _ => size;

                public RenderBoundsContract Build()
                    => RenderBoundsContract.Create(_map, static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG003"),
            "the delegate is built in a constructor, where it closes over that constructor's arguments");
    }

    /// <remarks>
    /// The field is only a name for the lambda, so the lambda answers for the same rule an argument lambda
    /// does. Accepting it because the field is readonly would let the whole rule be sidestepped by moving
    /// the callback one declaration away from the call.
    /// </remarks>
    [Test]
    public void ANonStaticLambdaInAReadonlyFieldInitialiser_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Author
            {
                private static readonly Func<Rect, Rect> s_map = value => value;

                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(s_map, static value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Contain("BESG003"));
    }

    /// <remarks>
    /// An instance method group keeps its receiver as the delegate's target, and the field being readonly
    /// says nothing about that receiver, so a field on it still changes what the callback answers.
    /// </remarks>
    [Test]
    public void AnInstanceMethodGroupInAReadonlyFieldInitialiser_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class Provider
            {
                public float Inset { get; set; }

                public Rect Map(Rect value) => value;
            }

            internal static class Author
            {
                private static readonly Func<Rect, Rect> s_map = new Provider().Map;

                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(s_map, static value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Contain("BESG003"));
    }

    /// <remarks>
    /// A readonly field holding a static lambda clears the capture rule and reaches static state exactly as
    /// a static lambda written at the call does, so BESG004 has to follow the callback into the field.
    /// </remarks>
    [Test]
    public void AReadonlyFieldHoldingAStaticLambdaOverMutableStaticState_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal static class Author
            {
                private static readonly Func<Rect, Rect> s_map = static value =>
                    new Rect(value.X + Settings.Offset, value.Y, value.Width, value.Height);

                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(s_map, static value => value);
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

    /// <remarks>
    /// A field compiled into another assembly carries no initialiser this rule can read, and readonly proves
    /// only that the reference is fixed. Not being able to look is the reporting side, as it already is for
    /// a get-only property whose getter has no source.
    /// </remarks>
    [Test]
    public void AReadonlyFieldFromAReferencedAssembly_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeWithLibrary(
            """
            using System;
            using Beutl.Graphics;

            namespace External
            {
                public static class Callbacks
                {
                    public static readonly Func<Rect, Rect> Map = static value => value;
                }
            }
            """,
            """
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;
            using External;

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(Callbacks.Map, static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG003"),
            "a field whose initialiser is not in this compilation proves nothing about what it holds");
    }

    /// <remarks>
    /// This is the shape the diagnostic leaves an author with: one delegate cached in a readonly field, built
    /// from a static lambda that reads nothing that can change. Rejecting it would make the rule unusable.
    /// </remarks>
    [Test]
    public void AReadonlyFieldHoldingAStaticLambdaOverConstants_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public const float Inset = 1f;
            }

            internal static class Author
            {
                private static readonly Func<Rect, Rect> s_map = static value =>
                    new Rect(value.X + Settings.Inset, value.Y, value.Width, value.Height);

                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(s_map, static value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// A property is the same "cannot prove it" shape a readonly field is: it names a delegate without
    /// saying what that delegate carries. Here the getter hands back a lambda that closes over the
    /// receiver, so the callback answers differently once a field on that receiver changes.
    /// </remarks>
    [Test]
    public void ACapturingLambdaBehindAGetOnlyProperty_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class Provider
            {
                private readonly Rect _size;

                public Provider(Rect size) => _size = size;

                public Func<Rect, Rect> Map => _ => _size;
            }

            internal static class Author
            {
                public static RenderBoundsContract Build(Provider provider)
                    => RenderBoundsContract.Create(provider.Map, static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG003"),
            "the getter returns a lambda that is not static, so what it closed over decides the answer");
    }

    /// <remarks>
    /// The getter clearing the capture rule leaves the static-state rule to decide, exactly as it does for a
    /// static lambda written at the call or held in a readonly field.
    /// </remarks>
    [Test]
    public void AGetOnlyPropertyReturningAStaticLambdaOverMutableStaticState_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal static class Provider
            {
                public static Func<Rect, Rect> Map => static value =>
                    new Rect(value.X + Settings.Offset, value.Y, value.Width, value.Height);
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(Provider.Map, static value => value);
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

    /// <remarks>
    /// A setter means the delegate the call sees is whatever was last assigned, which nothing in the
    /// declaration pins. Having no setter is what makes a getter worth reading at all.
    /// </remarks>
    [Test]
    public void ASettableDelegateProperty_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Provider
            {
                public static Func<Rect, Rect> Map { get; set; } = static value => value;
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(Provider.Map, static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG003"),
            "an assignable property says nothing about which delegate reaches the contract");
    }

    /// <remarks>
    /// A getter compiled into another assembly cannot be read, and having no setter says only that this
    /// declaration does not write it. Not being able to look is the reporting side here too.
    /// </remarks>
    [Test]
    public void AGetOnlyDelegatePropertyFromAReferencedAssembly_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeWithLibrary(
            """
            using System;
            using Beutl.Graphics;

            namespace External
            {
                public static class Callbacks
                {
                    public static Func<Rect, Rect> Map => static value => value;
                }
            }
            """,
            """
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;
            using External;

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(Callbacks.Map, static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG003"),
            "a getter with no source proves nothing about the delegate it returns");
    }

    /// <remarks>
    /// This is what stops the property arm becoming a blanket reject, which authors would suppress wholesale
    /// and lose both rules with it: the getter can only ever hand back one static lambda, and that lambda
    /// reads nothing that can change.
    /// </remarks>
    [Test]
    public void AGetOnlyPropertyReturningAStaticLambdaOverConstants_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public const float Inset = 1f;
            }

            internal static class Provider
            {
                public static Func<Rect, Rect> Map => static value =>
                    new Rect(value.X + Settings.Inset, value.Y, value.Width, value.Height);
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(Provider.Map, static value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// A definition builder's execution callback is retained on the definition and is what the description is
    /// fingerprinted by, so it is keyed and re-run on exactly the terms a metadata callback is. Nothing else
    /// checks it: the runtime validator null-checks the callback and never looks inside it.
    /// </remarks>
    [TestCase("OpaqueRenderDefinition", "OpaqueRenderSession")]
    [TestCase("TargetScopeDefinition", "TargetScopeSession")]
    [TestCase("TargetCommandDefinition", "TargetCommandSession")]
    [TestCase("RawTargetScopeDefinition", "RawTargetScopeSession")]
    [TestCase("RawTargetCommandDefinition", "RawTargetCommandSession")]
    [TestCase("GeometryDefinition", "GeometrySession")]
    public void ACapturingExecutionCallbackOnADefinitionBuilder_IsReported(string definition, string session)
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeDefinitionCallback(
            definition,
            session,
            "(session, state) => Use(session, state + inset)");

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG003"),
            "a callback that reads a value the caller supplies per recording is what the state parameter is for");
    }

    /// <remarks>
    /// These builders already carry a state-passing parameter, so the shape the diagnostic asks for is the
    /// one they were designed around and must stay silent.
    /// </remarks>
    [TestCase("OpaqueRenderDefinition", "OpaqueRenderSession")]
    [TestCase("TargetScopeDefinition", "TargetScopeSession")]
    [TestCase("TargetCommandDefinition", "TargetCommandSession")]
    [TestCase("RawTargetScopeDefinition", "RawTargetScopeSession")]
    [TestCase("RawTargetCommandDefinition", "RawTargetCommandSession")]
    [TestCase("GeometryDefinition", "GeometrySession")]
    public void AStaticExecutionCallbackTakingItsValuesThroughState_IsNotReported(
        string definition,
        string session)
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeDefinitionCallback(
            definition,
            session,
            "static (session, state) => Use(session, state)");

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// The bounds argument is there so the cases prove the rule reaches the delegate parameter alone: a
    /// definition's Create takes metadata and planner traits beside its callback, and none of those carry a
    /// closure to report.
    /// </remarks>
    private static ImmutableArray<Diagnostic> AnalyzeDefinitionCallback(
        string definition,
        string session,
        string callback)
        => Analyze($$"""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;
            using Beutl.Graphics.Effects;

            internal static class Author
            {
                public static void Build(float inset)
                    => {{definition}}<float>.Create(
                        {{callback}},
                        RenderBoundsContract.Create(static value => value, static value => value));

                private static void Use({{session}} session, float value)
                {
                }
            }
            """);

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

    /// <summary>
    /// Compiles the contract stubs and <paramref name="librarySource"/> into an assembly, then analyzes
    /// <paramref name="source"/> against it rather than against the stubs as source.
    /// </summary>
    /// <remarks>
    /// A member whose source the rule cannot read is a case the rule decides on its own terms, and the only
    /// way to produce one is to compile it somewhere else first.
    /// </remarks>
    private static ImmutableArray<Diagnostic> AnalyzeWithLibrary(string librarySource, string source)
    {
        CSharpCompilation library = CSharpCompilation.Create(
            "AnalyzerTestLibrary",
            [
                CSharpSyntaxTree.ParseText(ContractStubs),
                CSharpSyntaxTree.ParseText(librarySource),
            ],
            FrameworkReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var image = new MemoryStream();
        EmitResult emit = library.Emit(image);
        Assert.That(
            emit.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error),
            Is.Empty,
            "the referenced assembly must compile, or the assertions below prove nothing");

        image.Position = 0;
        return Analyze(source, MetadataReference.CreateFromStream(image));
    }

    private static ImmutableArray<Diagnostic> Analyze(string source, MetadataReference? library = null)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "AnalyzerTest",
            library is null
                ? [CSharpSyntaxTree.ParseText(ContractStubs), CSharpSyntaxTree.ParseText(source)]
                : [CSharpSyntaxTree.ParseText(source)],
            library is null ? FrameworkReferences : [.. FrameworkReferences, library],
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
