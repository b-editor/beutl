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

            public abstract class RenderNode { }

            public abstract class RenderResourceSlot { }

            public sealed class RenderResourceSlot<T> : RenderResourceSlot
                where T : class { }

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

            public sealed class ImmediateCanvas { }

            public sealed class RenderFragmentHandle { }

            public delegate void PaintedSourceDraw<TState>(ImmediateCanvas canvas, TState state);

            public sealed class RenderNodeContext
            {
                public RenderFragmentHandle PaintedSource<TState>(
                    TState state,
                    PaintedSourceDraw<TState> draw,
                    RenderBoundsContract bounds) => null!;

                public void PublishMappedInputs(Func<RenderFragmentHandle, RenderFragmentHandle> mapper) { }
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

            public sealed class ShaderUniformWriter { }

            public sealed class ShaderExecutionContext { }

            public sealed class ShaderBindingBuilder
            {
                public void Uniform<T>(
                    string name,
                    T value,
                    Action<ShaderUniformWriter, T, ShaderExecutionContext> bind) { }
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

    /// <remarks>
    /// The one reader the rule admits. A node's mapping is written against the node's own properties, and
    /// nothing else says what that mapping is; threading every such value through TState says the same
    /// thing at more length. What makes it safe is not the lambda but the node: it arrives as the
    /// delegate's own target, marking it changed re-records it, and an answer of its that moves between
    /// recording and metadata resolution fails the request at the recorded-answer cross-check.
    /// </remarks>
    [Test]
    public void ALambdaReadingOnlyTheDeclaringNode_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class ShiftedNode : RenderNode
            {
                public float Offset { get; private set; }

                public RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        value => new Rect(value.X + Offset, value.Y, value.Width, value.Height),
                        value => new Rect(value.X - this.Offset, value.Y, value.Width, value.Height));
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Not.Contain("BESG003"),
            "a node writing the mapping its own properties describe is the form the runtime accepts, so "
            + "the rule that stands in front of the runtime has to accept it too");
    }

    /// <remarks>
    /// The half of the split nothing else covers. The runtime validator reads the delegate's target, and a
    /// closure over a local arrives as a compiler display class that none of its type tests answer for, so
    /// this rule is the only thing between an author and a captured mutable local.
    /// </remarks>
    [Test]
    public void ALambdaClosingOverALocal_InsideANode_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class ShiftedNode : RenderNode
            {
                public RenderBoundsContract Build()
                {
                    float offset = 4f;
                    return RenderBoundsContract.Create(
                        value => new Rect(value.X + offset, value.Y, value.Width, value.Height),
                        static value => value);
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG003"),
            "nothing re-records when a local is assigned, so admitting the node must not admit this");
    }

    [Test]
    public void ALambdaClosingOverAParameter_InsideANode_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class ShiftedNode : RenderNode
            {
                public RenderBoundsContract Build(float offset)
                    => RenderBoundsContract.Create(
                        value => new Rect(value.X + offset, value.Y, value.Width, value.Height),
                        static value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Contain("BESG003"));
    }

    /// <remarks>
    /// Reading the node is a permission to read the node, not a permission to close over whatever else is
    /// in scope beside it.
    /// </remarks>
    [Test]
    public void ALambdaClosingOverTheNodeAndALocal_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class ShiftedNode : RenderNode
            {
                public float Offset { get; private set; }

                public RenderBoundsContract Build()
                {
                    float extra = 4f;
                    return RenderBoundsContract.Create(
                        value => new Rect(value.X + Offset + extra, value.Y, value.Width, value.Height),
                        static value => value);
                }
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Contain("BESG003"));
    }

    /// <remarks>
    /// The exemption follows the runtime's, which names <c>RenderNode</c> and nothing else. What holds a
    /// node's answer still is a node's: change marking re-records the node that owns the callback, and no
    /// such thing exists for an arbitrary object an author happens to write the lambda inside. Accepting it
    /// here would be the analyzer promising what the engine does not.
    /// </remarks>
    [Test]
    public void ALambdaReadingAnEnclosingInstanceThatIsNotANode_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class Provider
            {
                public float Offset { get; set; }

                public RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        value => new Rect(value.X + Offset, value.Y, value.Width, value.Height),
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG003"),
            "no change marking covers an ordinary object, so its state moving is caught by nothing");
    }

    /// <remarks>
    /// Staticness is still read first, so it decides the callback without the closure walk being consulted.
    /// </remarks>
    [Test]
    public void AStaticLambdaInsideANode_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class PassthroughNode : RenderNode
            {
                public RenderBoundsContract Build()
                    => RenderBoundsContract.Create(static value => value, static value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Not.Contain("BESG003"));
    }

    /// <remarks>
    /// A lambda that reads nothing is cached in a singleton the compiler owns, so there is no instance for
    /// the node test to disagree about and nothing for the plan key to stand wrongly for. It is accepted
    /// wherever it is written, which is what makes the rule about what a callback reads rather than about
    /// how it was spelled.
    /// </remarks>
    [Test]
    public void ANonStaticLambdaReadingNothing_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class Provider
            {
                public float Offset { get; set; }

                public RenderBoundsContract Build()
                    => RenderBoundsContract.Create(value => value, value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Not.Contain("BESG003"));
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
    /// The one reader the rule admits, written the other way. The runtime is handed the same delegate as
    /// for the lambda: the node as its target, and a method of the node's type as the structural identity
    /// the plan is keyed by. The method group is the narrower of the two forms - an instance method reads
    /// its receiver and its arguments, where a lambda has the enclosing scope to reach into - so reporting
    /// this while admitting the lambda would be judging how the mapping was spelled.
    /// </remarks>
    [Test]
    public void AMethodGroupOnTheDeclaringNode_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class ShiftedNode : RenderNode
            {
                public float Offset { get; private set; }

                public RenderBoundsContract Build()
                    => RenderBoundsContract.Create(Forward, this.Backward);

                private Rect Forward(Rect value)
                    => new Rect(value.X + Offset, value.Y, value.Width, value.Height);

                private Rect Backward(Rect value)
                    => new Rect(value.X - Offset, value.Y, value.Width, value.Height);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "the node is the delegate's target and one of its methods is the plan key either way, so the "
            + "rule cannot turn on whether the author wrote a lambda or named the method");
    }

    /// <remarks>
    /// The receiver is read at the call and not off the method, so a mapping a base node declares is judged
    /// by the node that runs it - which is the object that becomes the delegate's target.
    /// </remarks>
    [Test]
    public void AMethodGroupOnAMethodABaseNodeDeclares_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal abstract class ShiftingNode : RenderNode
            {
                public float Offset { get; private set; }

                protected Rect Forward(Rect value)
                    => new Rect(value.X + Offset, value.Y, value.Width, value.Height);
            }

            internal sealed class ShiftedNode : ShiftingNode
            {
                public RenderBoundsContract Build()
                    => RenderBoundsContract.Create(base.Forward, Forward);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Not.Contain("BESG003"));
    }

    /// <remarks>
    /// Parentheses around a receiver leave the same object underneath, and the whole point of this arm is
    /// that the rule answers by what the callback reads rather than by how it was spelled.
    /// </remarks>
    [Test]
    public void AMethodGroupOnAParenthesisedThis_InsideANode_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class ShiftedNode : RenderNode
            {
                public float Offset { get; private set; }

                public RenderBoundsContract Build()
                    => RenderBoundsContract.Create((this).Forward, ((this)).Forward);

                private Rect Forward(Rect value)
                    => new Rect(value.X + Offset, value.Y, value.Width, value.Height);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Not.Contain("BESG003"));
    }

    /// <remarks>
    /// The body is the whole of what a method group contributes, and the exemption says nothing about what
    /// that body reads, so BESG004 still follows it. Accepting the receiver must not stop the rule looking.
    /// </remarks>
    [Test]
    public void AMethodGroupOnTheDeclaringNodeReadingAMutableStatic_IsReportedByTheStaticStateRule()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal sealed class ShiftedNode : RenderNode
            {
                public RenderBoundsContract Build()
                    => RenderBoundsContract.Create(Forward, static value => value);

                private Rect Forward(Rect value)
                    => new Rect(value.X + Settings.Offset, value.Y, value.Width, value.Height);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Not.Contain("BESG003"),
            "the receiver is the declaring node, which is the reader the rule admits");
        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "admitting the receiver decides nothing about the body, which is still walked");
    }

    /// <remarks>
    /// The exemption is for the node the callback is written inside, not for whatever object a node holds.
    /// Nothing marks that object changed, and the runtime validator is handed it rather than the node.
    /// </remarks>
    [Test]
    public void AMethodGroupOnAnotherObject_InsideANode_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class Provider
            {
                public float Offset { get; set; }

                public Rect Map(Rect value)
                    => new Rect(value.X + Offset, value.Y, value.Width, value.Height);
            }

            internal sealed class ShiftedNode : RenderNode
            {
                private readonly Provider _other = new Provider();

                public RenderBoundsContract Build()
                    => RenderBoundsContract.Create(_other.Map, static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG003"),
            "the delegate's target is the provider, whose state no change marking on this node covers");
    }

    /// <remarks>
    /// The sharpest case for the exemption's boundary: the receiver is a <c>RenderNode</c> and is still not
    /// the node whose recording the callback belongs to, so marking it changed re-records the wrong node.
    /// </remarks>
    [Test]
    public void AMethodGroupOnAnotherNode_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class SourceNode : RenderNode
            {
                public float Offset { get; private set; }

                public Rect Map(Rect value)
                    => new Rect(value.X + Offset, value.Y, value.Width, value.Height);
            }

            internal sealed class ShiftedNode : RenderNode
            {
                private readonly SourceNode _source = new SourceNode();

                public RenderBoundsContract Build()
                    => RenderBoundsContract.Create(_source.Map, static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG003"),
            "being a node is not the test; being the node this callback is recorded for is");
    }

    /// <remarks>
    /// The receiver arm takes the exemption on the same terms the closure arm does, which the runtime
    /// validator writes as <c>not RenderNode</c>: an ordinary object is covered by no change marking, so
    /// its state moving is caught by nothing.
    /// </remarks>
    [Test]
    public void AMethodGroupOnThis_OutsideANode_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class Provider
            {
                public float Offset { get; set; }

                public RenderBoundsContract Build()
                    => RenderBoundsContract.Create(Map, this.Map);

                private Rect Map(Rect value)
                    => new Rect(value.X + Offset, value.Y, value.Width, value.Height);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG003"),
            "no change marking covers an ordinary object, so its receiver moving is caught by nothing");
    }

    /// <remarks>
    /// A struct's own <c>this</c> is boxed at the conversion exactly as a named value-typed receiver is, so
    /// the delegate answers from a copy of what the receiver held right there. The exemption names a class
    /// and cannot reach this whatever the struct is written inside.
    /// </remarks>
    [Test]
    public void AMethodGroupOnAStructsOwnThis_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal readonly record struct Metadata(float Inset)
            {
                public RenderBoundsContract Build()
                    => RenderBoundsContract.Create(Map, this.Map);

                private Rect Map(Rect value)
                    => new Rect(value.X + Inset, value.Y, value.Width, value.Height);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Contain("BESG003"));
    }

    /// <remarks>
    /// A bare name is not always a receiver. A local function that is not declared static reads the scope
    /// it is written in exactly as a lambda does, and nothing here reads which locals it took, so widening
    /// the receiver arm must not widen to this.
    /// </remarks>
    [Test]
    public void ALocalFunctionCapturingAParameter_InsideANode_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class ShiftedNode : RenderNode
            {
                public RenderBoundsContract Build(float offset)
                {
                    Rect Forward(Rect value)
                        => new Rect(value.X + offset, value.Y, value.Width, value.Height);

                    return RenderBoundsContract.Create(Forward, static value => value);
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG003"),
            "the delegate carries the caller's argument, which is the case this rule exists for");
    }

    /// <remarks>
    /// Staticness is still read first, so it decides the callback without the receiver being consulted.
    /// </remarks>
    [Test]
    public void AStaticMethodGroupInsideANode_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class PassthroughNode : RenderNode
            {
                public RenderBoundsContract Build()
                    => RenderBoundsContract.Create(Map, Map);

                private static Rect Map(Rect value) => value;
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
            "a getter has to prove its value, and a call is not a shape that proves one");
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
    /// readonly stops the field being assigned and says nothing about the object it points at. The delegate
    /// is the same delegate on every frame, so the plan key never moves, while what the callback reads
    /// through the field answers differently the moment anyone writes <c>Offset</c> - the same failure a
    /// settable static field is reported for, reached one hop further out.
    /// </remarks>
    [Test]
    public void AStaticLambdaReadingThroughAStaticReadonlyFieldOfAMutableClass_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeCallbackReading(
            """
            internal sealed class Palette
                {
                    public float Offset;
                }

                public static readonly Palette Current = new Palette();
            """,
            "value.X + Settings.Current.Offset");

        Assert.Multiple(() =>
        {
            Assert.That(
                diagnostics.Select(static d => d.Id),
                Does.Contain("BESG004"),
                "readonly fixes the reference, and the object behind it is what the callback reads");
            Assert.That(
                diagnostics.Select(static d => d.Id),
                Does.Not.Contain("BESG003"),
                "the lambda is static, so the capture rule is right to stay silent");
        });
    }

    /// <remarks>
    /// A readonly struct cannot be written through an instance member, which says nothing about what its
    /// fields point at. This one holds a reference, so the value the field keeps is fixed and the state the
    /// callback reaches through it is not - the case that stops "readonly struct" from standing in for
    /// "immutable".
    /// </remarks>
    [Test]
    public void AStaticLambdaReadingThroughAStaticReadonlyFieldOfAReferenceBearingStruct_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeCallbackReading(
            """
            internal sealed class Palette
                {
                    public float Offset;
                }

                internal readonly struct Style
                {
                    public Style(Palette palette) => Palette = palette;

                    public Palette Palette { get; }
                }

                public static readonly Style Current = new Style(new Palette());
            """,
            "value.X + Settings.Current.Palette.Offset");

        Assert.Multiple(() =>
        {
            Assert.That(
                diagnostics.Select(static d => d.Id),
                Does.Contain("BESG004"),
                "a readonly struct holding a reference carries the mutable object with it");
            Assert.That(
                diagnostics.Select(static d => d.Id),
                Does.Not.Contain("BESG003"),
                "the lambda is static, so the capture rule is right to stay silent");
        });
    }

    /// <remarks>
    /// The field's type is decided in metadata, where the rule still has the fields to walk even though it
    /// has no source. A referenced readonly struct that carries a reference must be reported on the same
    /// terms as one written here, or the boundary itself becomes the way past the rule.
    /// </remarks>
    [Test]
    public void AStaticLambdaReadingThroughAStaticReadonlyFieldFromAReferencedAssembly_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeWithLibrary(
            """
            namespace External
            {
                public sealed class Palette
                {
                    public float Offset;
                }

                public readonly struct Style
                {
                    public Style(Palette palette) => Palette = palette;

                    public Palette Palette { get; }
                }
            }
            """,
            """
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;
            using External;

            internal static class Settings
            {
                public static readonly Style Current = new Style(new Palette());
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            value.X + Settings.Current.Palette.Offset,
                            value.Y,
                            value.Width,
                            value.Height),
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "a type with no source is still a type whose fields metadata carries");
    }

    /// <remarks>
    /// A compilation imports only the public and protected members of a metadata type, so a class read from
    /// another assembly arrives with its private state absent from the field list and every field still on
    /// it readonly. Reading that list as "no writable state" answers a question the walk was never shown the
    /// evidence for: mutating the object behind the field changes what the callback answers while the plan
    /// key stays the same. The library here is emitted whole, so this is not a reference assembly having
    /// removed anything - it is the import boundary, and a reference assembly only reaches the same place
    /// twice.
    /// </remarks>
    [Test]
    public void AStaticLambdaReadingAStaticReadonlyFieldOfASealedClassWithMetadataOnlyState_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeWithLibrary(
            """
            namespace External
            {
                public sealed class Palette
                {
                    private float _offset;

                    public float Offset => _offset;

                    public void Shift(float amount) => _offset += amount;
                }
            }
            """,
            """
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;
            using External;

            internal static class Settings
            {
                public static readonly Palette Current = new Palette();
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            value.X + Settings.Current.Offset,
                            value.Y,
                            value.Width,
                            value.Height),
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "a field list stopping at the public members is not evidence the type carries no state");
    }

    /// <remarks>
    /// StringBuilder's shape, which is the case that showed the hole: sealed, its buffer private, and a
    /// public Length computed from it. Across an assembly boundary the buffer is not imported, so the walk
    /// sees a class with nothing writable on it; Append then changes what Length answers with the delegate
    /// untouched.
    /// </remarks>
    [Test]
    public void AStaticLambdaReadingABuilderLengthAcrossAnAssemblyBoundary_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeWithLibrary(
            """
            namespace External
            {
                public sealed class Buffer
                {
                    private char[] _chunk = new char[8];
                    private int _length;

                    public int Length => _length;

                    public void Append(char value)
                    {
                        _chunk[_length] = value;
                        _length++;
                    }
                }
            }
            """,
            """
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;
            using External;

            internal static class Settings
            {
                public static readonly Buffer Current = new Buffer();
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            value.X + Settings.Current.Length,
                            value.Y,
                            value.Width,
                            value.Height),
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "a builder whose buffer was not imported is still a builder");
    }

    /// <remarks>
    /// The same class written here instead. Sealing and having nothing writable is not what was in doubt -
    /// where the field list was read from is - so the two have to land on opposite sides, or the rule is
    /// deciding on the shape of the type rather than on what it was shown.
    /// </remarks>
    [Test]
    public void AStaticLambdaReadingAStaticReadonlyFieldOfAStatelessSealedClassFromAnotherAssembly_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeWithLibrary(
            """
            namespace External
            {
                public sealed class Address
                {
                    public float Measure(float value) => value;
                }
            }
            """,
            """
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;
            using External;

            internal static class Settings
            {
                public static readonly Address Current = new Address();
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            Settings.Current.Measure(value.X),
                            value.Y,
                            value.Width,
                            value.Height),
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "a class with no fields in the metadata is not a class with no fields");
    }

    /// <remarks>
    /// A struct is where the import stops short of nothing: a compilation cannot decide definite assignment
    /// or an unmanaged constraint without every field of a referenced struct, so it has them all and the
    /// walk is reading the type. Rejecting a referenced readonly struct would cost the rule every value type
    /// an author shares across a project boundary for no evidence gained.
    /// </remarks>
    [Test]
    public void AStaticLambdaReadingAStaticReadonlyFieldOfAnImmutableStructFromAnotherAssembly_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeWithLibrary(
            """
            namespace External
            {
                public readonly struct Inset
                {
                    private readonly float _top;

                    public Inset(float top) => _top = top;

                    public float Top => _top;
                }
            }
            """,
            """
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;
            using External;

            internal static class Settings
            {
                public static readonly Inset Current = new Inset(2f);
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            value.X + Settings.Current.Top,
                            value.Y,
                            value.Width,
                            value.Height),
                        static value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// The pair below is why EffectiveScale.Unbounded is a field. A sentinel of an immutable struct is the
    /// same constant in either form to a source caller, and only one of the two survives the assembly
    /// boundary: the getter's body is not imported, so the rule is left holding a signature that a computed
    /// getter and a constant one share, while the struct's fields are imported and carry initonly with them.
    /// Both directions are pinned together because the property case is the whole reason the field case
    /// matters - accepting the field proves nothing unless the property it replaced was refused.
    /// </remarks>
    [Test]
    public void AStaticLambdaReadingAGetOnlySentinelPropertyFromAnotherAssembly_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeWithLibrary(
            SentinelLibrary,
            SentinelAuthor("Density.AsProperty"));

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "a getter's body does not cross the assembly boundary, so a sentinel returning default is "
                + "indistinguishable from one computing its result");
    }

    [Test]
    public void AStaticLambdaReadingAStaticReadonlySentinelFieldFromAnotherAssembly_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeWithLibrary(
            SentinelLibrary,
            SentinelAuthor("Density.AsField"));

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "the field is initonly and the struct's fields are imported, so the rule can read the whole of "
                + "what the sentinel carries");
    }

    /// <summary>
    /// A sentinel of an immutable struct offered in both forms, so the two tests above differ only in which
    /// one the callback reads.
    /// </summary>
    private const string SentinelLibrary = """
        namespace External
        {
            public readonly struct Density
            {
                private readonly bool _bounded;
                private readonly float _value;

                public static Density AsProperty => default;

                public static readonly Density AsField;

                public float Value => _bounded ? _value : 1f;
            }
        }
        """;

    private static string SentinelAuthor(string sentinel) => $$"""
        using Beutl.Graphics;
        using Beutl.Graphics.Rendering;
        using External;

        internal static class Author
        {
            public static RenderBoundsContract Build()
                => RenderBoundsContract.Create(
                    static value => new Rect(
                        value.X + {{sentinel}}.Value,
                        value.Y,
                        value.Width,
                        value.Height),
                    static value => value);
        }
        """;

    /// <remarks>
    /// The shape the diagnostic's own message sends an author to, seen the way an author outside
    /// Beutl.Engine sees it: a metadata class, which the field walk is no longer allowed to clear. The rule
    /// knows this one by name instead, so recommending it and accepting it stay the same answer whichever
    /// assembly the author is writing in.
    /// </remarks>
    [Test]
    public void AStaticLambdaReadingAStaticReadonlyResourceSlotFromAnotherAssembly_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeWithLibrary(
            """
            namespace External
            {
                public sealed class Payload
                {
                    public float Offset;
                }
            }
            """,
            """
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;
            using External;

            internal static class Author
            {
                private static readonly RenderResourceSlot<Payload> s_slot = new();

                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            value.X + Lease(s_slot),
                            value.Y,
                            value.Width,
                            value.Height),
                        static value => value);

                private static float Lease(RenderResourceSlot<Payload> slot) => 0f;
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// The reviewer's example written literally, against the shipping StringBuilder. Its five chunk fields
    /// are internal, so a compilation importing only public and protected members sees a sealed class with
    /// no fields at all - which is why the walk used to accept it, and why the walk is not what decides a
    /// metadata type any more.
    /// </remarks>
    [Test]
    public void AStaticLambdaReadingAStaticReadonlyStringBuilderLength_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeCallbackReading(
            "public static readonly System.Text.StringBuilder Current = new System.Text.StringBuilder();",
            "value.X + Settings.Current.Length");

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "appending to the builder changes what the callback answers with the delegate unchanged");
    }

    /// <remarks>
    /// These are what stop the field rule becoming a blanket reject on static readonly, which authors would
    /// suppress wholesale and lose the rule with it. Each type's instances carry no state anything can
    /// write, so a field fixing the value fixes the whole of what the callback can read through it.
    /// </remarks>
    [TestCase("public static readonly Alignment Current = Alignment.Center;", "value.X + (float)Settings.Current")]
    [TestCase("public static readonly float Current = 2f;", "value.X + Settings.Current")]
    [TestCase("public static readonly string Current = \"beutl\";", "value.X + Settings.Current.Length")]
    [TestCase("public static readonly decimal Current = 1.5m;", "value.X + (float)Settings.Current")]
    [TestCase("public static readonly System.DateTime Current = System.DateTime.MinValue;", "value.X + Settings.Current.Year")]
    public void AStaticLambdaReadingAStaticReadonlyFieldOfAnImmutableType_IsNotReported(
        string members,
        string read)
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeCallbackReading(members, read);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// The struct an author writes to group a few numbers is the shape the field rule has to keep accepting,
    /// or the reference-bearing case above would have cost every value type with it.
    /// </remarks>
    [Test]
    public void AStaticLambdaReadingThroughAStaticReadonlyFieldOfAnImmutableStruct_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeCallbackReading(
            """
            internal readonly struct Margins
                {
                    public Margins(float left, Inset inset)
                    {
                        Left = left;
                        Inset = inset;
                    }

                    public float Left { get; }

                    public Inset Inset { get; }
                }

                internal readonly struct Inset
                {
                    public Inset(float top) => Top = top;

                    public float Top { get; }
                }

                public static readonly Margins Current = new Margins(1f, new Inset(2f));
            """,
            "value.X + Settings.Current.Left + Settings.Current.Inset.Top");

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// A class with nothing to write is the shape the diagnostic's own advice hands back: a resource slot is
    /// an address a definition declares once, held in a static readonly field and named by every callback
    /// that leases through it. Rejecting it for being a reference would leave the rule rejecting the fix it
    /// recommends, which is the state authors suppress a rule over.
    /// </remarks>
    [Test]
    public void AStaticLambdaReadingAStaticReadonlyFieldOfAStatelessSealedClass_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeCallbackReading(
            """
            internal sealed class Address
                {
                    public float Measure(float value) => value;
                }

                public static readonly Address Current = new Address();
            """,
            "Settings.Current.Measure(value.X)");

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// Every field readonly and every field's type accepted is the whole of what the walk asks, and a class
    /// answering it carries no more writable state than a readonly struct does.
    /// </remarks>
    [Test]
    public void AStaticLambdaReadingThroughAStaticReadonlyFieldOfASealedImmutableClass_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeCallbackReading(
            """
            internal sealed class Config
                {
                    public Config(float offset) => Offset = offset;

                    public float Offset { get; }
                }

                public static readonly Config Current = new Config(2f);
            """,
            "value.X + Settings.Current.Offset");

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// Sealing is what makes the field list the whole of the type. Without it the walk would be reading one
    /// class while the field holds an instance of another, so an unsealed class is reported however
    /// immutable its own declaration looks.
    /// </remarks>
    [Test]
    public void AStaticLambdaReadingThroughAStaticReadonlyFieldOfAnUnsealedClass_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeCallbackReading(
            """
            internal class Config
                {
                    public Config(float offset) => Offset = offset;

                    public float Offset { get; }
                }

                public static readonly Config Current = new Config(2f);
            """,
            "value.X + Settings.Current.Offset");

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "a subclass can add the writable state the declaration does not show");
    }

    /// <remarks>
    /// A settable auto-property compiles to a backing field nothing marks readonly, so the walk reaches the
    /// same answer it would for the field written out - which is what stops an author spelling their way
    /// past the rule.
    /// </remarks>
    [Test]
    public void AStaticLambdaReadingThroughAStaticReadonlyFieldOfAClassWithASettableProperty_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeCallbackReading(
            """
            internal sealed class Config
                {
                    public float Offset { get; set; }
                }

                public static readonly Config Current = new Config();
            """,
            "value.X + Settings.Current.Offset");

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "the backing field of a settable auto-property is writable state like any other");
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
    /// does - and here that rule accepts it. A field initialiser has no local, no parameter and no
    /// <see langword="this"/> in scope, so the lambda it holds closes over nothing whether or not it says
    /// <c>static</c>, and the compiler caches it in a singleton. That the field is not what decided this is
    /// what <see cref="ACapturingLambdaAssignedToAReadonlyField_IsReported"/> and
    /// <see cref="ACapturingLambdaBehindAGetOnlyProperty_IsReported"/> pin: the rule follows the member to
    /// what it holds rather than accepting the member.
    /// </remarks>
    [Test]
    public void ANonStaticLambdaInAReadonlyFieldInitialiser_IsNotReported()
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

        Assert.That(diagnostics.Select(static d => d.Id), Does.Not.Contain("BESG003"));
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
    /// A static method group clears the capture rule and reads static state on exactly the terms a static
    /// lambda does, so the body it names has to be read for the same reason the lambda's is. Leaving it
    /// unread exempted the very form BESG003's own message tells authors to write.
    /// </remarks>
    [Test]
    public void AStaticMethodGroupReadingAMutableStatic_IsReported()
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
                private static Rect Map(Rect value)
                    => new Rect(value.X + Settings.Offset, value.Y, value.Width, value.Height);

                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(Map, static value => value);
            }
            """);

        Assert.Multiple(() =>
        {
            Assert.That(
                diagnostics.Select(static d => d.Id),
                Does.Contain("BESG004"),
                "the same body written as a lambda is reported, and it is the same program");
            Assert.That(
                diagnostics.Select(static d => d.Id),
                Does.Not.Contain("BESG003"),
                "a static method group is delegate-cached, so the capture rule is right to stay silent");
        });
    }

    /// <remarks>
    /// The call the callback makes used to be where the rule stopped, so a read moved one method along was
    /// enough to leave it. The walk now follows it to a bounded depth, on the shape the field walk already
    /// uses: run out of depth and report, so the bound can cost a diagnostic and never hide one.
    /// </remarks>
    [Test]
    public void AStaticLambdaCallingAMethodThatReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeCallbackReading(
            "public static float Current;\n\n    public static float Read() => Current;",
            "value.X + Settings.Read()");

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "moving the read one call along does not make the callback answer the same way twice");
    }

    /// <remarks>
    /// Both halves at once: the callback is a method group, and the read is a call further in. Neither the
    /// body inspection nor the call walk catches this on its own.
    /// </remarks>
    [Test]
    public void AStaticMethodGroupReadingAMutableStaticThroughACall_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;

                public static float Read() => Offset;
            }

            internal static class Author
            {
                private static Rect Map(Rect value)
                    => new Rect(value.X + Settings.Read(), value.Y, value.Width, value.Height);

                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(Map, static value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Contain("BESG004"));
    }

    /// <remarks>
    /// A chain longer than the walk is reported rather than accepted, so the bound only ever costs a
    /// diagnostic. Nine hops is one past <c>MaxCallbackCallDepth</c>, and every body in it is provably
    /// constant, which is what makes the report attributable to the bound and to nothing else.
    /// </remarks>
    [Test]
    public void ACallChainDeeperThanTheWalk_IsReported()
    {
        string chain = string.Join(
            "\n\n    ",
            Enumerable
                .Range(0, 9)
                .Select(static i => $"public static float Step{i}() => Step{i + 1}();"));

        ImmutableArray<Diagnostic> diagnostics = AnalyzeCallbackReading(
            $"{chain}\n\n    public static float Step9() => 1f;",
            "value.X + Settings.Step0()");

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "running out of depth is not evidence that the rest of the chain is constant");
    }

    /// <remarks>
    /// A callback that names its own method recursively would walk for ever, and the compiler allows it.
    /// </remarks>
    [Test]
    public void ARecursiveStaticMethodGroupOverConstants_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Author
            {
                private static Rect Map(Rect value)
                    => value.Width > 0f ? Map(new Rect(value.X, value.Y, 0f, value.Height)) : value;

                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(Map, static value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// This is the half that stops the widened rule from rejecting the form it recommends. A method group
    /// reading only constants and proven static readonly state is exactly what BESG003 sends authors to, and
    /// reporting it would leave them nowhere to go.
    /// </remarks>
    [Test]
    public void AStaticMethodGroupReadingOnlyProvenConstants_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public const float Inset = 1f;

                public static readonly float Margin = 2f;

                public static float Scale => 3f;
            }

            internal static class Author
            {
                private static Rect Map(Rect value)
                    => new Rect(
                        value.X + Settings.Inset + Settings.Margin + Settings.Scale,
                        value.Y,
                        value.Width,
                        value.Height);

                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(Map, static value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// A method compiled into another assembly carries no body this rule can read, and it is the whole of
    /// the callback: staying silent would say the rule looked when it looked at nothing. This is where the
    /// static field rule already stands - a type whose state was not imported is refused, not assumed clean -
    /// and a callback is the one place that reasoning bites hardest, because there is no second half left to
    /// check.
    /// </remarks>
    [Test]
    public void AStaticMethodGroupFromAReferencedAssembly_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeWithLibrary(
            """
            using Beutl.Graphics;

            namespace External
            {
                public static class Callbacks
                {
                    public static Rect Map(Rect value) => value;
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

        Assert.Multiple(() =>
        {
            Assert.That(
                diagnostics.Select(static d => d.Id),
                Does.Contain("BESG004"),
                "a body with no source proves nothing, and here it is the entire callback");
            Assert.That(
                diagnostics.Select(static d => d.Id),
                Does.Not.Contain("BESG003"),
                "the method group is still delegate-cached, so the capture rule is right to stay silent");
        });
    }

    /// <remarks>
    /// A method the body calls is the documented bound, and it stays one when the callee has no source: the
    /// rule did inspect the callback, unlike the case above where the callback was the unreadable method.
    /// Reporting here would reject every callback that names <see cref="Math.Clamp(float, float, float)"/>.
    /// </remarks>
    [Test]
    public void AStaticLambdaCallingAMethodWithNoSource_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeCallbackReading(
            "public const float Inset = 1f;",
            "System.Math.Clamp(value.X, 0f, Settings.Inset)");

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// A constructor runs code the body never names: the object-creation expression names the type, and the
    /// loop that reads the body sees only names. So a helper whose constructor snapshots a mutable static
    /// answers differently on a later frame while the delegate keying the plan stays the same one.
    /// </remarks>
    [Test]
    public void AStaticLambdaConstructingAHelperThatReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal sealed class OffsetSnapshot
            {
                public OffsetSnapshot()
                {
                    Value = Settings.Offset;
                }

                public float Value { get; }
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            value.X + new OffsetSnapshot().Value, value.Y, value.Width, value.Height),
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "moving the read into a constructor does not make the callback answer the same way twice");
    }

    /// <remarks>
    /// An initialiser runs as part of every constructor of the type, so it is reached by constructing the
    /// type and is not written inside any constructor body.
    /// </remarks>
    [Test]
    public void AStaticLambdaConstructingAHelperWhoseInitialiserReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal sealed class OffsetSnapshot
            {
                public float Value { get; } = Settings.Offset;
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            value.X + new OffsetSnapshot().Value, value.Y, value.Width, value.Height),
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "a property initialiser is constructor code the callback never names");
    }

    [Test]
    public void AStaticLambdaConstructingARecordOverItsArguments_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed record Inset(float Amount);

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            value.X + new Inset(1f).Amount, value.Y, value.Width, value.Height),
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "a primary constructor over its own arguments reads nothing that changes between recordings");
    }

    [Test]
    public void AStaticLambdaConstructingAStruct_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal readonly struct Inset
            {
                public Inset(float amount) => Amount = amount;

                public float Amount { get; }
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            value.X + new Inset(1f).Amount, value.Y, value.Width, value.Height),
                        static value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// A type with no constructor of its own has one the compiler writes, which has no source to read and no
    /// state to reach; reporting it would reject every callback that names a plain helper.
    /// </remarks>
    [Test]
    public void AStaticLambdaConstructingATypeWithAnImplicitConstructor_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class Inset
            {
                public float Amount { get; init; }
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            value.X + new Inset { Amount = 1f }.Amount,
                            value.Y,
                            value.Width,
                            value.Height),
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "an object initialiser assigns values already in the body, which the walk already reads");
    }

    [Test]
    public void AStaticLambdaConstructingACollectionOverConstants_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System.Collections.Generic;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            value.X + new List<float> { 1f, 2f }.Count, value.Y, value.Width, value.Height),
                        static value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// A chained constructor is spelled <c>this</c>, which is not a name the walk's loop reads, so the body
    /// it runs is reached only by following the chain.
    /// </remarks>
    [Test]
    public void AStaticLambdaConstructingAHelperThatChainsToAReadingConstructor_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal sealed class OffsetSnapshot
            {
                public OffsetSnapshot() : this(Settings.Offset)
                {
                }

                private OffsetSnapshot(float value) => Value = value;

                public float Value { get; }
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            value.X + new OffsetSnapshot().Value, value.Y, value.Width, value.Height),
                        static value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Contain("BESG004"));
    }

    /// <remarks>
    /// A constructor with no initialiser still runs its base type's parameterless one, and that call is
    /// written nowhere at all. The middle type has no constructor of its own, so the chain runs through one
    /// the compiler wrote: stopping there would lose a base this rule can read.
    /// </remarks>
    [Test]
    public void AStaticLambdaConstructingATypeWhoseBaseConstructorReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal abstract class Snapshot
            {
                protected Snapshot() => Value = Settings.Offset;

                public float Value { get; }
            }

            internal abstract class NamedSnapshot : Snapshot
            {
            }

            internal sealed class OffsetSnapshot : NamedSnapshot
            {
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            value.X + new OffsetSnapshot().Value, value.Y, value.Width, value.Height),
                        static value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Contain("BESG004"));
    }

    /// <remarks>
    /// An operator is spelled as punctuation, so the same read moved behind one used to leave the rule with
    /// nothing to look at.
    /// </remarks>
    [Test]
    public void AStaticLambdaUsingAnOperatorThatReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal readonly struct Inset
            {
                public Inset(float amount) => Amount = amount;

                public float Amount { get; }

                public static Inset operator +(Inset left, Inset right)
                    => new Inset(left.Amount + right.Amount + Settings.Offset);
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            value.X + (new Inset(1f) + new Inset(2f)).Amount,
                            value.Y,
                            value.Width,
                            value.Height),
                        static value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Contain("BESG004"));
    }

    /// <remarks>
    /// An implicit conversion is spelled nothing at all - a declared type decides it - so an author can move
    /// a read behind one without changing a single name in the callback.
    /// </remarks>
    [Test]
    public void AStaticLambdaUsingAnImplicitConversionThatReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal readonly struct Scaled
            {
                public Scaled(float value) => Value = value;

                public float Value { get; }

                public static implicit operator Scaled(float value)
                    => new Scaled(value * Settings.Offset);
            }

            internal static class Author
            {
                private static float Take(Scaled scaled) => scaled.Value;

                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(Take(value.X), value.Y, value.Width, value.Height),
                        static value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Contain("BESG004"));
    }

    [Test]
    public void AStaticLambdaUsingAnOperatorOverItsOperands_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal readonly struct Inset
            {
                public Inset(float amount) => Amount = amount;

                public float Amount { get; }

                public static Inset operator +(Inset left, Inset right)
                    => new Inset(left.Amount + right.Amount);
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            value.X + (new Inset(1f) + new Inset(2f)).Amount,
                            value.Y,
                            value.Width,
                            value.Height),
                        static value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// A constructor with no source here is the callee case, not the callback case: the rule did read the
    /// callback, so this is a bound on an inspected callback and reporting it would reject every callback
    /// that constructs a framework type.
    /// </remarks>
    [Test]
    public void AStaticLambdaConstructingATypeFromAReferencedAssembly_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeWithLibrary(
            """
            namespace External
            {
                public sealed class Inset
                {
                    public Inset(float amount) => Amount = amount;

                    public float Amount { get; }
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
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            value.X + new Inset(1f).Amount, value.Y, value.Width, value.Height),
                        static value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    /// <remarks>
    /// A helper's method is a body the callback runs, and the expression that makes the helper says
    /// everything the walk needs: the type is exact, and what the instance carries came from the constructor
    /// the walk already reads. So following it needs no model of a receiver, and stopping at it let an
    /// author move a read one member sideways and keep the rule silent.
    /// </remarks>
    [Test]
    public void AStaticLambdaCallingAMethodOnAFreshlyConstructedHelperThatReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal sealed class Shifter
            {
                public float Shift(float value) => value + Settings.Offset;
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            new Shifter().Shift(value.X), value.Y, value.Width, value.Height),
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "a read moved into a helper's method is still a read the callback reaches");
    }

    /// <remarks>
    /// A virtual method is the one case the receiver decides, and an object creation decides it here: the
    /// expression names the exact type it makes, so the override the call binds to is the override that
    /// runs. That is read off the call site, not tracked from anywhere.
    /// </remarks>
    [Test]
    public void AStaticLambdaCallingAVirtualMethodOnAFreshlyConstructedHelperThatReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal class Shifter
            {
                public virtual float Shift(float value) => value + Settings.Offset;
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            new Shifter().Shift(value.X), value.Y, value.Width, value.Height),
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "new names the exact type, so the virtual call has one body and it is the one that runs");
    }

    [Test]
    public void AStaticLambdaReadingAPropertyThatReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal sealed class OffsetView
            {
                public float Value => Settings.Offset;
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            value.X + new OffsetView().Value, value.Y, value.Width, value.Height),
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "a getter is a body the callback runs as surely as a method is");
    }

    /// <remarks>
    /// An indexer is a getter reached through punctuation rather than a name, so a walk that only looked at
    /// names could not see it at all.
    /// </remarks>
    [Test]
    public void AStaticLambdaReadingAnIndexerThatReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal sealed class OffsetTable
            {
                public float this[int index] => Settings.Offset;
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            value.X + new OffsetTable()[0], value.Y, value.Width, value.Height),
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "an indexer runs a getter body the source never spells a name for");
    }

    /// <remarks>
    /// The walk reports static reads, so a body that reads only its arguments and the instance it was
    /// called on has nothing in it to report however far the walk goes into it. Following instance members
    /// must not turn an ordinary helper into a diagnostic.
    /// </remarks>
    [Test]
    public void AStaticLambdaCallingAnInstanceMethodOverItsArguments_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class Scaler
            {
                private readonly float _factor;

                public Scaler(float factor) => _factor = factor;

                public float Scale(float value) => value * _factor;
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            new Scaler(2f).Scale(value.X), value.Y, value.Width, value.Height),
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "a helper reading its own arguments and fields answers the same way twice");
    }

    /// <remarks>
    /// <para>
    /// The bound this rule stops at, stated as a case, and it holds whether or not the callee could be
    /// overridden - what is missing is the receiver, not the body.
    /// </para>
    /// <para>
    /// Walking past it was tried and is not viable. These callbacks are handed the objects they work
    /// through - a session, a canvas, a context - so a member called on one of those is the engine behind
    /// it: following them reported the render backend's loggers, its shared GPU context, its dispatcher and
    /// its pools, hundreds of times over a tree that is correct, none of which says anything about whether
    /// a callback answers the same way twice. A rule that loud is a rule authors suppress.
    /// </para>
    /// </remarks>
    [Test]
    [TestCase("public")]
    [TestCase("public virtual")]
    public void AStaticLambdaCallingAMethodOnAReceiverItDidNotCreate_IsNotReported(string modifiers)
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze($$"""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal class Shifter
            {
                {{modifiers}} float Shift(float value) => value + Settings.Offset;
            }

            internal static class Author
            {
                private static float Apply(Shifter shifter, float value) => shifter.Shift(value);

                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            Apply(new Shifter(), value.X), value.Y, value.Width, value.Height),
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "the callback did not make this receiver, so what it carries is not read off the call site");
    }

    /// <remarks>
    /// A receiver the expression did not make can still be one this rule reads the making of: a readonly
    /// field whose one initialiser is an object creation names the exact type it makes as surely as writing
    /// the creation at the call site does, and no constructor can put a different instance there. A
    /// stateless helper kept as a singleton clears the static field rule on its own - readonly, and a
    /// sealed class carrying no instance state - so before this the mutable static its method reads was
    /// reported by nothing at all.
    /// </remarks>
    [Test]
    public void AStaticLambdaCallingAMethodOnAStaticReadonlyStatelessHelperThatReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal sealed class Shifter
            {
                public float Shift(float value) => value + Settings.Offset;
            }

            internal static class Helpers
            {
                public static readonly Shifter Shared = new Shifter();
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            Helpers.Shared.Shift(value.X), value.Y, value.Width, value.Height),
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "a singleton held in a readonly field is an instance whose exact type and whose making are "
            + "both readable, so the body it runs is the body that runs");
    }

    /// <remarks>
    /// The same helper held by the node instead of by a static class, which is the shape the capture rule
    /// admits: the lambda reads nothing but its own node, so BESG003 is silent by design, and what the node
    /// holds is where a mutable static could sit with neither rule looking at it.
    /// </remarks>
    [Test]
    public void ANodeLambdaCallingAReadonlyFieldHelperThatReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal sealed class Shifter
            {
                public float Shift(float value) => value + Settings.Offset;
            }

            internal sealed class ShiftedNode : RenderNode
            {
                private readonly Shifter _shifter = new Shifter();

                public RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        value => new Rect(
                            _shifter.Shift(value.X), value.Y, value.Width, value.Height),
                        value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Not.Contain("BESG003"),
            "the lambda reads its own node and nothing else, which is the one reader the capture rule "
            + "admits");

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "admitting the node is a permission to read the node, not a permission to reach a mutable "
            + "static through a helper the node holds");
    }

    /// <remarks>
    /// What an instance carries came from its constructor, so a constructor reading a mutable static is the
    /// same impurity as a method reading one - and the method called on that instance hands the captured
    /// value back without naming the static anywhere the walk over the callback could see it. The rule
    /// already reads the constructor of a creation written at the call site, which it reaches as an
    /// expression of the body; a creation held in a readonly field is written where the walk never goes, so
    /// following the receiver has to carry the constructor with it or the two spellings answer differently.
    /// </remarks>
    [Test]
    public void ANodeLambdaCallingAReadonlyFieldHelperWhoseConstructorReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal sealed class Shifter
            {
                private readonly float _offset;

                public Shifter() => _offset = Settings.Offset;

                public float Shift(float value) => value + _offset;
            }

            internal sealed class ShiftedNode : RenderNode
            {
                private readonly Shifter _shifter = new Shifter();

                public RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        value => new Rect(
                            _shifter.Shift(value.X), value.Y, value.Width, value.Height),
                        value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "the constructor is where the mutable static entered the instance, and a method reading only "
            + "its own field still answers differently between two recordings because of it");
    }

    /// <remarks>
    /// readonly fixes the reference against everything except the declaring type's own constructors, so the
    /// initialiser is only the whole story where none of them writes the field. Here the constructor puts a
    /// subclass there whose override reads nothing, so the body the walk would follow is not the body that
    /// runs and reporting it would be a diagnostic about code the callback never reaches.
    /// </remarks>
    [Test]
    public void ANodeLambdaCallingAReadonlyFieldHelperAConstructorReplaces_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal class Shifter
            {
                public virtual float Shift(float value) => value + Settings.Offset;
            }

            internal sealed class QuietShifter : Shifter
            {
                public override float Shift(float value) => value;
            }

            internal sealed class ShiftedNode : RenderNode
            {
                private readonly Shifter _shifter = new Shifter();

                public ShiftedNode() => _shifter = new QuietShifter();

                public RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        value => new Rect(
                            _shifter.Shift(value.X), value.Y, value.Width, value.Height),
                        value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Not.Contain("BESG004"),
            "a constructor that writes the field puts an instance there the initialiser does not name, so "
            + "the initialiser is not evidence of what the callback runs");
    }

    /// <remarks>
    /// The rule follows the member the call binds to, and that is chosen by the declared type of the
    /// expression. A creation written at the call site is its own declared type, but a field declared as a
    /// base of what its initialiser makes is not: the walk would read the base body the override replaces,
    /// and report a read the instance in that field never makes. So the exact type is what clears the gate.
    /// </remarks>
    [Test]
    public void ANodeLambdaCallingAReadonlyFieldDeclaredAsABaseOfWhatItHolds_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal class Shifter
            {
                public virtual float Shift(float value) => value + Settings.Offset;
            }

            internal sealed class QuietShifter : Shifter
            {
                public override float Shift(float value) => value;
            }

            internal sealed class ShiftedNode : RenderNode
            {
                private readonly Shifter _shifter = new QuietShifter();

                public RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        value => new Rect(
                            _shifter.Shift(value.X), value.Y, value.Width, value.Height),
                        value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Not.Contain("BESG004"),
            "the call binds against the field's declared type, so the body the walk would read is not the "
            + "override the created instance runs");
    }

    /// <remarks>
    /// The field has to be one the callback reaches on its own. A field read off a receiver the callback
    /// was handed is that receiver's state, and following it is the walk into the engine behind a session
    /// or a canvas that this rule stops at - the very thing the parameter case above pins - so a readonly
    /// field of a handed-in object is no more followed than the object is.
    /// </remarks>
    [Test]
    public void AStaticLambdaCallingAMethodOnAReadonlyFieldOfAReceiverItWasHanded_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal sealed class Shifter
            {
                public float Shift(float value) => value + Settings.Offset;
            }

            internal sealed class Toolbox
            {
                public readonly Shifter Shifter = new Shifter();
            }

            internal static class Author
            {
                private static float Apply(Toolbox toolbox, float value) => toolbox.Shifter.Shift(value);

                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            Apply(new Toolbox(), value.X), value.Y, value.Width, value.Height),
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "the callback did not make this receiver, and what it holds in a readonly field is still its "
            + "own state and not something read off the call site");
    }

    /// <remarks>
    /// A conditional access spells its receiver once, at the head of the chain, so the name beside the call
    /// carries none of its own. That is a different syntax shape and not a different rule: the object
    /// creation guarding the chain is still made right there, so the exact type and everything the instance
    /// carries are still read off the call site. Reading only the receiver written beside the name let an
    /// author move a read behind a question mark and keep the id silent.
    /// </remarks>
    [Test]
    public void AStaticLambdaConditionallyCallingAMethodOnAFreshHelperThatReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal sealed class Shifter
            {
                public float Shift(float value) => value + Settings.Offset;
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            new Shifter()?.Shift(value.X) ?? value.X,
                            value.Y,
                            value.Width,
                            value.Height),
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "the chain still makes the instance it runs on, however the call is spelled");
    }

    /// <remarks>
    /// The other side of the same shape: a question mark does not make a receiver visible. What the walk
    /// needs is the object creation at the head of the chain, and a chain headed by a parameter has none, so
    /// this stays where the rule already stops.
    /// </remarks>
    [Test]
    public void AStaticLambdaConditionallyCallingAMethodOnAReceiverItDidNotCreate_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal sealed class Shifter
            {
                public float Shift(float value) => value + Settings.Offset;
            }

            internal static class Author
            {
                private static float Apply(Shifter shifter, float value)
                    => shifter?.Shift(value) ?? value;

                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            Apply(new Shifter(), value.X), value.Y, value.Width, value.Height),
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "the callback did not make this receiver, so what it carries is not read off the call site");
    }

    /// <remarks>
    /// Following the conditional spelling must not turn an ordinary helper into a diagnostic either: a body
    /// reading only its arguments and the fields its own constructor set has nothing in it to report.
    /// </remarks>
    [Test]
    public void AStaticLambdaConditionallyCallingAMethodOverItsArguments_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class Scaler
            {
                private readonly float _factor;

                public Scaler(float factor) => _factor = factor;

                public float Scale(float value) => value * _factor;
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            new Scaler(2f)?.Scale(value.X) ?? value.X,
                            value.Y,
                            value.Width,
                            value.Height),
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "a helper reading its own arguments and fields answers the same way twice");
    }

    /// <remarks>
    /// <para>
    /// An extension method written in instance form is a static method spelled as if it were not. Roslyn
    /// hands the call site a reduced symbol whose <c>IsStatic</c> is false, so the staticness gate skipped
    /// it, and the receiver gate could not admit it either: the receiver is the value the callback was
    /// handed, not one it made. That put a static body the author can write and the walk never reads behind
    /// nothing more than a dot.
    /// </para>
    /// <para>
    /// Following it needs no receiver reasoning at all, which is what separates this from the bound below.
    /// The method the call runs is <c>ReducedFrom</c>, a static method whose every parameter - the receiver
    /// included - is an argument the call site passes, so there is no instance whose contents the rule
    /// would have to know.
    /// </para>
    /// </remarks>
    [Test]
    public void AStaticLambdaCallingASourceExtensionThatReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal static class RectExtensions
            {
                public static Rect Shift(this Rect value)
                    => new Rect(value.X + Settings.Offset, value.Y, value.Width, value.Height);
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => value.Shift(),
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "the extension is a static method, so the read inside it is one the walk can reach without "
            + "knowing anything about the receiver");
    }

    /// <remarks>
    /// The same call written the way it is declared. Both spellings run the one method, so a rule that
    /// answered them differently would be a rule an author escapes by adding a dot.
    /// </remarks>
    [Test]
    public void AStaticLambdaCallingASourceExtensionInStaticFormThatReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal static class RectExtensions
            {
                public static Rect Shift(this Rect value)
                    => new Rect(value.X + Settings.Offset, value.Y, value.Width, value.Height);
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => RectExtensions.Shift(value),
                        static value => value);
            }
            """);

        Assert.That(diagnostics.Select(static d => d.Id), Does.Contain("BESG004"));
    }

    /// <remarks>
    /// The other side of the same reach. An extension body that reads only what the call site passed it has
    /// nothing in it to report, and following extensions must not turn one into a diagnostic.
    /// </remarks>
    [Test]
    public void AStaticLambdaCallingASourceExtensionOverItsArguments_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class RectExtensions
            {
                public static Rect Inflate(this Rect value, float amount)
                    => new Rect(value.X - amount, value.Y - amount, value.Width, value.Height);
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => value.Inflate(2f),
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "an extension reading only its own parameters answers the same way twice");
    }

    /// <remarks>
    /// The documented bound, reached through the new spelling: a callee with no source here stops the walk
    /// without reporting, because the rule did read the callback and this is a bound on an inspected one.
    /// Answering otherwise would report every callback that names a LINQ operator.
    /// </remarks>
    [Test]
    public void AStaticLambdaCallingAnExtensionWithNoSource_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeWithLibrary(
            """
            namespace External.Library
            {
                using Beutl.Graphics;

                public static class RectExtensions
                {
                    public static float Offset;

                    public static Rect Shift(this Rect value)
                        => new Rect(value.X + Offset, value.Y, value.Width, value.Height);
                }
            }
            """,
            """
            using External.Library;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => value.Shift(),
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "nothing of that body is in this compilation, so the walk stops where it always stops");
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

    /// <remarks>
    /// A drawing callback and the mapping declared beside it are decided by one rule, so an author cannot
    /// find that one argument of a call reports what the next argument accepts.
    /// </remarks>
    [Test]
    public void ADrawCallbackClosingOverALocal_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class PaintingNode : RenderNode
            {
                public void Record(RenderNodeContext context)
                {
                    float offset = 4f;
                    context.PaintedSource(
                        0,
                        (canvas, state) => Consume(offset),
                        RenderBoundsContract.Create(static value => value, static value => value));
                }

                private static void Consume(float value) { }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG003"),
            "a drawing callback is retained and re-run exactly as a definition's execute is, so a local it "
            + "reads lets one plan key stand for two different drawings");
    }

    [Test]
    public void ADrawCallbackReadingOnlyTheDeclaringNode_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class PaintingNode : RenderNode
            {
                public float Offset { get; private set; }

                public void Record(RenderNodeContext context)
                    => context.PaintedSource(
                        0,
                        (canvas, state) => Consume(Offset),
                        RenderBoundsContract.Create(static value => value, static value => value));

                private static void Consume(float value) { }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Not.Contain("BESG003"),
            "the node the drawing is written inside is the one reader both paths admit");
    }

    /// <remarks>
    /// A shader binder is retained by the description and keyed by which declaration it is, so it takes the
    /// same answer as a drawing callback rather than the answer its enclosing type would give.
    /// </remarks>
    [Test]
    public void AShaderBinderReadingOnlyTheDeclaringNode_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics.Effects;
            using Beutl.Graphics.Rendering;

            internal sealed class ScalingNode : RenderNode
            {
                public float Scale { get; private set; }

                public void Declare(ShaderBindingBuilder bindings)
                    => bindings.Uniform("amount", 1f, (writer, value, context) => Consume(value * Scale));

                private static void Consume(float value) { }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Not.Contain("BESG003"));
    }

    [Test]
    public void AShaderBinderClosingOverALocal_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics.Effects;
            using Beutl.Graphics.Rendering;

            internal sealed class ScalingNode : RenderNode
            {
                public void Declare(ShaderBindingBuilder bindings)
                {
                    float scale = 2f;
                    bindings.Uniform("amount", 1f, (writer, value, context) => Consume(value * scale));
                }

                private static void Consume(float value) { }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG003"));
    }

    /// <remarks>
    /// What naming one method rather than its type leaves out. A recording context's input mapper runs
    /// while the call is being made and is never retained, so nothing keys a plan by it and there is no
    /// second answer for a captured value to produce.
    /// </remarks>
    [Test]
    public void ACapturingInputMapperOnTheSameContext_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics.Rendering;

            internal sealed class MappingNode : RenderNode
            {
                public void Record(RenderNodeContext context)
                {
                    RenderFragmentHandle replacement = null!;
                    context.PublishMappedInputs(input => replacement);
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Not.Contain("BESG003"));
    }

    /// <remarks>
    /// <para>
    /// An event is a delegate field whose value is a subscriber list, and += and -= are its assignments.
    /// Reading that list back is only legal inside the declaring type, which is exactly where a callback
    /// written beside the event sits, so the narrowness of the language rule is no protection here.
    /// </para>
    /// <para>
    /// Nothing about the callback changes when a subscriber is added: the delegate is the same method, the
    /// plan key is the same key, and the bounds this hands back are different.
    /// </para>
    /// </remarks>
    [Test]
    public void AStaticLambdaReadingAStaticEventDeclaredBesideIt_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static event Action Changed;

                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => Changed is null ? value : new Rect(0f, 0f, 1f, 1f),
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "a subscriber added anywhere in the program changes what this callback answers while the "
            + "delegate the plan is keyed by stays the same method");
    }

    /// <remarks>
    /// The shape that carries the hazard past the declaring type. The callback itself may be written
    /// anywhere - only the helper the walk follows into has to sit beside the event - so "you can only
    /// read an event from inside its own type" bounds where the read is written, not where it is reached
    /// from.
    /// </remarks>
    [Test]
    public void AStaticLambdaCallingAHelperThatReadsAStaticEvent_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static event Action Changed;

                public static bool IsExpanded() => Changed is not null;
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => Settings.IsExpanded() ? new Rect(0f, 0f, 1f, 1f) : value,
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "the walk follows the static method, and the read it finds there is the same read whatever "
            + "type the callback was written in");
    }

    /// <remarks>
    /// The write side, which needs no declaring type at all: += binds from anywhere. A mutable static field
    /// is reported wherever the callback names it, assignment included, and an event is the same state
    /// under a keyword.
    /// </remarks>
    [Test]
    public void AStaticLambdaSubscribingToAStaticEvent_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static event Action Changed;
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            Settings.Changed += static () => { };
                            return value;
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "a callback that rewrites static state on every evaluation is the impurity this rule is for, "
            + "whichever side of the += the state sits on");
    }

    /// <remarks>
    /// The same state one indirection out, and the reachable one: a static readonly singleton needs no
    /// declaring-type relationship to the callback at all. A source type's member list carries the event
    /// and its accessors and not the delegate field the compiler writes behind them, so a type whose whole
    /// mutable state is an event used to pass the immutability walk that the identical state spelled as
    /// <c>private Action _changed;</c> fails.
    /// </remarks>
    [Test]
    public void AStaticLambdaReachingAStaticReadonlyHelperWhoseOnlyStateIsAnEvent_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class Notifier
            {
                public event Action Changed;

                public float Shift(float value) => Changed is null ? value : value + 1f;
            }

            internal static class Helpers
            {
                public static readonly Notifier Shared = new Notifier();
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            Helpers.Shared.Shift(value.X), value.Y, value.Width, value.Height),
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "readonly fixes which Notifier this is and not what it holds, and what it holds is a "
            + "subscriber list any += rewrites");
    }

    /// <remarks>
    /// The negative control for the field-like case: an event declared with its own accessors has no
    /// backing field, so a type carrying nothing but one carries no state, and reporting it would be the
    /// rule reading the keyword rather than the storage. Whatever such accessors do write is a field the
    /// walk already sees.
    /// </remarks>
    [Test]
    public void AStaticLambdaReachingAStaticReadonlyHelperWhoseEventHasNoBackingField_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class Notifier
            {
                public event Action Changed { add { } remove { } }

                public float Shift(float value) => value + 1f;
            }

            internal static class Helpers
            {
                public static readonly Notifier Shared = new Notifier();
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            Helpers.Shared.Shift(value.X), value.Y, value.Width, value.Height),
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "an event with written accessors stores nothing of its own, so this helper carries no more "
            + "state than a stateless one");
    }

    /// <remarks>
    /// The negative control for the naming: nameof reaches the event without reading the list, exactly as
    /// it already does for a mutable static field.
    /// </remarks>
    [Test]
    public void AStaticLambdaNamingAStaticEventInsideNameof_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static event Action Changed;

                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            nameof(Changed).Length, value.Y, value.Width, value.Height),
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "nameof spells the member and reads nothing of it");
    }

    /// <remarks>
    /// The negative control for the scope: this rule is about static state, and an event the node itself
    /// declares is the node's own, which BESG003 admits a callback reading and which change marking
    /// re-records. Reporting it here would take back the one reader both rules are built around.
    /// </remarks>
    [Test]
    public void ANodeLambdaReadingAnEventItsOwnNodeDeclares_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class NotifyingNode : RenderNode
            {
                public event Action Changed;

                public RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        value => Changed is null ? value : new Rect(0f, 0f, 1f, 1f),
                        value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "the node the callback is written inside is the one reader both rules admit, and an event of "
            + "its own is no more static than a field of its own");
    }

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
