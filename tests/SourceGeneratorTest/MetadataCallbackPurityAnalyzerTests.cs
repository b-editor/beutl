using System.Collections.Immutable;
using System.IO;
using Beutl.Engine.SourceGenerators.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;

namespace SourceGeneratorTest;

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

            public sealed class RenderResourceSlot<T>
                where T : class { }

            public sealed class OpaqueRenderSession { }

            public sealed class TargetScopeSession { }

            public sealed class TargetCommandSession { }

            public sealed class RawTargetScopeSession { }

            public sealed class RawTargetCommandSession { }

            public sealed class OpaqueRenderDescription
            {
                public static OpaqueRenderDescription Create<TState>(
                    TState state,
                    Action<OpaqueRenderSession, TState> execute,
                    RenderBoundsContract bounds) => null!;

                internal static OpaqueRenderDescription CreateRequestLocal(
                    Action<OpaqueRenderSession> execute,
                    RenderBoundsContract bounds) => null!;
            }

            public sealed class TargetScopeDescription
            {
                public static TargetScopeDescription Create<TState>(
                    TState state,
                    Action<TargetScopeSession, TState> execute,
                    RenderBoundsContract bounds) => null!;

                internal static TargetScopeDescription CreateRequestLocal(
                    Action<TargetScopeSession> execute,
                    RenderBoundsContract bounds) => null!;
            }

            public sealed class TargetCommandDescription
            {
                public static TargetCommandDescription Create<TState>(
                    TState state,
                    Action<TargetCommandSession, TState> execute,
                    RenderBoundsContract bounds) => null!;

                internal static TargetCommandDescription CreateRequestLocal(
                    Action<TargetCommandSession> execute,
                    RenderBoundsContract bounds) => null!;
            }

            public sealed class RawTargetScopeDescription
            {
                public static RawTargetScopeDescription Create<TState>(
                    TState state,
                    Action<RawTargetScopeSession, TState> execute,
                    RenderBoundsContract bounds) => null!;

                internal static RawTargetScopeDescription CreateRequestLocal(
                    Action<RawTargetScopeSession> execute,
                    RenderBoundsContract bounds) => null!;
            }

            public sealed class RawTargetCommandDescription
            {
                public static RawTargetCommandDescription Create<TState>(
                    TState state,
                    Action<RawTargetCommandSession, TState> execute,
                    RenderBoundsContract bounds) => null!;

                internal static RawTargetCommandDescription CreateRequestLocal(
                    Action<RawTargetCommandSession> execute,
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

        namespace Beutl.Graphics.Shaders
        {
            using System;
            using Beutl.Graphics.Rendering;

            public sealed class ShaderUniformWriter { }

            public sealed class ShaderExecutionContext { }

            public sealed class ShaderBindingBuilder
            {
                public void Uniform<T>(
                    string name,
                    T value,
                    Action<ShaderUniformWriter, T, ShaderExecutionContext> bind) { }
            }

            public sealed class ShaderDescription
            {
                public static ShaderDescription CurrentPixel(
                    string source,
                    Action<ShaderBindingBuilder>? bindings = null) => null!;

                public static ShaderDescription WholeSource(
                    string source,
                    RenderBoundsContract bounds,
                    Action<ShaderBindingBuilder>? bindings = null) => null!;
            }
        }

        namespace Beutl.Graphics.Effects
        {
            using System;
            using Beutl.Graphics.Rendering;

            public sealed class GeometrySession { }

            public sealed class GeometryDescription
            {
                public static GeometryDescription Create<TState>(
                    TState state,
                    Action<GeometrySession, TState> render,
                    RenderBoundsContract bounds) => null!;

                internal static GeometryDescription CreateRequestLocal(
                    Action<GeometrySession> render,
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

    [TestCase("OpaqueRenderDescription", "OpaqueRenderSession")]
    [TestCase("TargetScopeDescription", "TargetScopeSession")]
    [TestCase("TargetCommandDescription", "TargetCommandSession")]
    [TestCase("RawTargetScopeDescription", "RawTargetScopeSession")]
    [TestCase("RawTargetCommandDescription", "RawTargetCommandSession")]
    [TestCase("GeometryDescription", "GeometrySession")]
    public void ACapturingExecutionCallbackOnADescriptionFactory_IsReported(
        string description,
        string session)
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeDescriptionCallback(
            description,
            session,
            "(session, state) => Use(session, state + inset)");

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG003"),
            "a callback that reads a value the caller supplies per recording is what the state parameter is for");
    }

    [TestCase("OpaqueRenderDescription", "OpaqueRenderSession")]
    [TestCase("TargetScopeDescription", "TargetScopeSession")]
    [TestCase("TargetCommandDescription", "TargetCommandSession")]
    [TestCase("RawTargetScopeDescription", "RawTargetScopeSession")]
    [TestCase("RawTargetCommandDescription", "RawTargetCommandSession")]
    [TestCase("GeometryDescription", "GeometrySession")]
    public void AStaticExecutionCallbackOnADescriptionFactory_IsNotReported(
        string description,
        string session)
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeDescriptionCallback(
            description,
            session,
            "static (session, state) => Use(session, state)");

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

    [TestCase("OpaqueRenderDescription", "OpaqueRenderSession")]
    [TestCase("TargetScopeDescription", "TargetScopeSession")]
    [TestCase("TargetCommandDescription", "TargetCommandSession")]
    [TestCase("RawTargetScopeDescription", "RawTargetScopeSession")]
    [TestCase("RawTargetCommandDescription", "RawTargetCommandSession")]
    [TestCase("GeometryDescription", "GeometrySession")]
    public void ACapturingCallbackOnARequestLocalFactory_IsNotReported(
        string description,
        string session)
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeRequestLocalCallback(
            description,
            session,
            "session => Use(session, inset)");

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

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

    [Test]
    public void AStaticLambdaCallingAMethodWithNoSource_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeCallbackReading(
            "public const float Inset = 1f;",
            "System.Math.Clamp(value.X, 0f, Settings.Inset)");

        Assert.That(diagnostics.Select(static d => d.Id), Is.Empty);
    }

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

    [Test]
    public void AStaticLambdaUsingACollectionInitializerWhoseAddReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using System.Collections;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal sealed class BoundsBuilder : IEnumerable
            {
                public Rect Bounds;

                public void Add(Rect value)
                    => Bounds = new Rect(
                        value.X + Settings.Offset, value.Y, value.Width, value.Height);

                public IEnumerator GetEnumerator() => throw new NotSupportedException();
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new BoundsBuilder { value }.Bounds,
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "a collection initialiser runs an Add body the source never spells a name for");
    }

    [Test]
    public void AStaticLambdaUsingAMultiArgumentCollectionInitializerElementWhoseAddReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using System.Collections;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal sealed class BoundsBuilder : IEnumerable
            {
                public Rect Bounds;

                public void Add(float x, float y)
                    => Bounds = new Rect(x + Settings.Offset, y, 1f, 1f);

                public IEnumerator GetEnumerator() => throw new NotSupportedException();
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new BoundsBuilder { { value.X, value.Y } }.Bounds,
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "the multi-argument element form runs the same Add and has to be read the same way");
    }

    [Test]
    public void AStaticLambdaUsingACollectionInitializerWhoseAddReadsNothingStatic_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using System.Collections;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal sealed class BoundsBuilder : IEnumerable
            {
                public Rect Bounds;

                public void Add(Rect value) => Bounds = value;

                public IEnumerator GetEnumerator() => throw new NotSupportedException();
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new BoundsBuilder { value }.Bounds,
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "an Add that reads only what it was handed answers the same way twice");
    }

    [Test]
    public void AStaticLambdaUsingACollectionInitializerOnAMemberItDidNotCreate_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using System.Collections;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal class BoundsBuilder : IEnumerable
            {
                public Rect Bounds;

                public virtual void Add(Rect value)
                    => Bounds = new Rect(
                        value.X + Settings.Offset, value.Y, value.Width, value.Height);

                public IEnumerator GetEnumerator() => throw new NotSupportedException();
            }

            internal sealed class Owner
            {
                public BoundsBuilder Builder { get; } = new();
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Owner { Builder = { value } }.Builder.Bounds,
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "the callback did not make this receiver, so what its Add runs is not read off the call site");
    }

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

    [Test]
    public void ANodeLambdaCallingAReadonlyFieldHelperWhoseConstructorReadsAMutableStatic_IsNotReported()
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
            Does.Not.Contain("BESG004"),
            "the constructor ran once before the first recording, so what it read is the same value at "
            + "every recording and cannot be what makes the callback answer differently");
    }

    [Test]
    public void AStaticLambdaReachingASingletonWhoseConstructorReadsAMutableStatic_IsNotReported()
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
            Does.Not.Contain("BESG004"),
            "a value the static initialiser froze before the first recording is the same value at the "
            + "second, whatever the static it was read from does in between");
    }

    [Test]
    public void AStaticLambdaCreatingAHelperWhoseConstructorReadsAMutableStatic_IsReported()
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
            "the constructor runs inside the callback, so the static it reads is read again at every "
            + "recording");
    }

    [Test]
    public void AStaticLambdaHoldingAHelperInALocalWhoseConstructorReadsAMutableStatic_IsReported()
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

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            Shifter shifter = new Shifter();
                            return new Rect(shifter.Shift(value.X), value.Y, value.Width, value.Height);
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "the creation the local names is written inside the callback, so its constructor runs again "
            + "at every recording");
    }

    [Test]
    public void AStaticLambdaReachingAHelperThroughAGetOnlyPropertyAlias_IsReported()
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
                private static readonly Shifter s_shared = new Shifter();

                public static Shifter Shared => s_shared;
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
            "the getter hands back the field and nothing else, so the instance the call runs on is as "
            + "exactly known as one the expression makes");
    }

    [Test]
    public void AGetOnlyPropertyAliasToAStatelessHelper_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class Shifter
            {
                public float Shift(float value) => value + 1f;
            }

            internal static class Helpers
            {
                private static readonly Shifter s_shared = new Shifter();

                public static Shifter Shared => s_shared;
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
            "the hop is a way to reach a body, not a reason to report one");
    }

    [Test]
    public void AGetOnlyPropertyAliasToASettableField_IsNotFollowed()
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
                private static Shifter s_shared = new Shifter();

                public static Shifter Shared => s_shared;
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
            diagnostics.Select(static d => d.GetMessage()),
            Has.Exactly(1).Contains("the static property 'Helpers.Shared'"),
            "the getter is reported because a settable field can answer with a second instance");

        Assert.That(
            diagnostics,
            Has.Exactly(1).Items,
            "and that is the whole of it: the helper's body is not the body the getter was shown to run");
    }

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

    [Test]
    public void ANodeLambdaCallingAReadonlyFieldWhoseOverrideReadsAMutableStatic_IsReported()
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
                public virtual float Shift(float value) => value;
            }

            internal sealed class LoudShifter : Shifter
            {
                public override float Shift(float value) => value + Settings.Offset;
            }

            internal sealed class ShiftedNode : RenderNode
            {
                private readonly Shifter _shifter = new LoudShifter();

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
            "the field holds an instance made as LoudShifter for its whole life, so the override is what "
            + "the call runs and the declaration it binds to is not evidence of anything");
    }

    /// <remarks>
    /// The control for the case above: the same spelling with the read on the other side of the override,
    /// which reports only if the walk answers to the shape rather than to the body it runs.
    /// </remarks>
    [Test]
    public void ANodeLambdaCallingAReadonlyFieldWhoseOverrideReadsNothingStatic_IsNotReported()
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
            "the override the created instance runs reads nothing static, and the base body it replaces is "
            + "not a body the callback ever reaches");
    }

    [Test]
    public void ANodeLambdaReadingAReadonlyFieldWhosePropertyOverrideReadsAMutableStatic_IsReported()
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
                public virtual float Shift => 0f;
            }

            internal sealed class LoudShifter : Shifter
            {
                public override float Shift => Settings.Offset;
            }

            internal sealed class ShiftedNode : RenderNode
            {
                private readonly Shifter _shifter = new LoudShifter();

                public RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        value => new Rect(
                            value.X + _shifter.Shift, value.Y, value.Width, value.Height),
                        value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "a getter dispatches the same way a method does, so reading through the base declaration reads "
            + "the override's body");
    }

    [Test]
    public void ANodeLambdaCallingAReadonlyFieldDeclaredAsAnInterfaceItImplements_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal interface IShifter
            {
                float Shift(float value);
            }

            internal sealed class Shifter : IShifter
            {
                public float Shift(float value) => value + Settings.Offset;
            }

            internal sealed class ShiftedNode : RenderNode
            {
                private readonly IShifter _shifter = new Shifter();

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
            "an interface declaration has no body to read, and the implementation the made instance "
            + "carries does");
    }

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

    [Test]
    public void AStaticLambdaDeclaringALocalFunctionItNeverCalls_IsNotReported()
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
                        static value =>
                        {
                            static float Unused() => Settings.Offset;

                            return value;
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "nothing calls it, so the callback answers the same way twice whatever that body names");
    }

    [Test]
    public void AStaticLambdaDeclaringALocalFunctionThatOnlyCallsItself_IsNotReported()
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
                        static value =>
                        {
                            static float Unused(int depth)
                                => depth <= 0 ? Settings.Offset : Unused(depth - 1);

                            return value;
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "the only call to it is its own, and the callback never makes the first one");
    }

    [Test]
    public void AStaticLambdaCallingALocalFunctionThatReadsAMutableStatic_IsReported()
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
                        static value =>
                        {
                            static float Read() => Settings.Offset;

                            return new Rect(Read(), value.Y, value.Width, value.Height);
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "the call runs that body, and moving the read into a local function does not change what the "
            + "callback answers");
    }

    [Test]
    public void AStaticLambdaCallingANonStaticLocalFunctionThatReadsAMutableStatic_IsReported()
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
                        static value =>
                        {
                            float Read() => value.X + Settings.Offset;

                            return new Rect(Read(), value.Y, value.Width, value.Height);
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "the call runs that body whether or not the local function says static");
    }

    [Test]
    public void AStaticLambdaHoldingALambdaItNeverInvokes_IsNotReported()
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
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            Func<float> unused = static () => Settings.Offset;

                            return value;
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "the delegate is made and dropped, so that body is never a body the callback runs");
    }

    [Test]
    public void AStaticLambdaInvokingALambdaThatReadsAMutableStatic_IsReported()
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
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            Func<float> read = static () => Settings.Offset;

                            return new Rect(read(), value.Y, value.Width, value.Height);
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "the callback invokes it, so that read is one the callback makes");
    }

    [Test]
    public void AStaticLambdaPassingALambdaThatReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using System.Linq;
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
                        static value =>
                        {
                            float[] widths = [value.Width];

                            return new Rect(
                                widths.Select(static width => width + Settings.Offset).First(),
                                value.Y,
                                value.Width,
                                value.Height);
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "the operator runs that body, and the rule cannot read the operator to find out how often");
    }

    [Test]
    public void AStaticLambdaDeconstructingAValueWhoseDeconstructReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal readonly struct BoundsState
            {
                public void Deconstruct(out float x, out float y)
                {
                    x = Settings.Offset;
                    y = 0f;
                }
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            var (x, y) = new BoundsState();

                            return new Rect(x, y, value.Width, value.Height);
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "the deconstruction runs that body once per call, exactly as a named call would");
    }

    [Test]
    public void AStaticLambdaDeconstructingATuple_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            var (x, y) = (value.X, value.Y);

                            return new Rect(x, y, value.Width, value.Height);
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "a tuple deconstruction runs no method at all, so there is nothing to follow");
    }

    [Test]
    public void AStaticLambdaDeconstructingAPositionalRecord_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed record BoundsState(float X, float Y);

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            var (x, y) = new BoundsState(1f, 2f);

                            return new Rect(x, y, value.Width, value.Height);
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "nothing the compiler synthesised reads anything the rule has to prove");
    }

    [Test]
    public void AStaticLambdaDeconstructingAValueWhoseDeconstructReadsItsOwnFields_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal readonly struct BoundsState
            {
                private readonly float _x;
                private readonly float _y;

                public BoundsState(float x, float y)
                {
                    _x = x;
                    _y = y;
                }

                public void Deconstruct(out float x, out float y)
                {
                    x = _x;
                    y = _y;
                }
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            var (x, y) = new BoundsState(value.X, value.Y);

                            return new Rect(x, y, value.Width, value.Height);
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "the body reads the instance the callback made and nothing static, which is what following a "
            + "Deconstruct is for");
    }

    [Test]
    public void AStaticLambdaDeconstructingInAForEachWhoseDeconstructReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal readonly struct BoundsState
            {
                public void Deconstruct(out float x, out float y)
                {
                    x = Settings.Offset;
                    y = 0f;
                }
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            float width = 0f;
                            foreach (var (x, y) in new BoundsState[1])
                                width += x + y;

                            return new Rect(value.X, value.Y, width, value.Height);
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "the loop header runs that body once per element, which is more than once per call and not "
            + "less");
    }

    [Test]
    public void AStaticLambdaWhoseNestedDeconstructReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal readonly struct Inner
            {
                public void Deconstruct(out float x, out float y)
                {
                    x = Settings.Offset;
                    y = 0f;
                }
            }

            internal readonly struct Outer
            {
                public void Deconstruct(out float width, out Inner inner)
                {
                    width = 0f;
                    inner = default;
                }
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            var (width, (x, y)) = new Outer();

                            return new Rect(x, y, width, value.Height);
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "the inner Deconstruct runs too, and a read moved one level in is still a read the callback "
            + "makes");
    }

    /// <summary>Runs a loop over the sequence a case declares, inside a callback that reads its result.</summary>
    /// <remarks>
    /// Exactly one member of the sequence is impure in each reported case, so a report can only have come
    /// from the member that case is about, and the accepted cases keep the same shape so that silence
    /// means the walk looked and found nothing rather than that it never looked.
    /// </remarks>
    private static ImmutableArray<Diagnostic> AnalyzeIteration(string declarations, string loop)
        => Analyze($$"""
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            {{declarations}}

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            float width = 0f;
                            {{loop}}

                            return new Rect(value.X, value.Y, width, value.Height);
                        },
                        static value => value);
            }
            """);

    [Test]
    public void AStaticLambdaWhoseForEachGetEnumeratorReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeIteration(
            """
            internal sealed class Widths
            {
                public Enumerator GetEnumerator()
                {
                    _ = Settings.Offset;
                    return new Enumerator();
                }
            }

            internal struct Enumerator
            {
                public float Current => 0f;

                public bool MoveNext() => false;
            }
            """,
            """
            foreach (float item in new Widths())
                width += item;
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "the loop asks the sequence for that enumerator before its first pass, which is a call the "
            + "callback makes and spells nowhere");
    }

    [Test]
    public void AStaticLambdaWhoseForEachMoveNextReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeIteration(
            """
            internal sealed class Widths
            {
                public Enumerator GetEnumerator() => new Enumerator();
            }

            internal struct Enumerator
            {
                public float Current => 0f;

                public bool MoveNext() => Settings.Offset > 0f;
            }
            """,
            """
            foreach (float item in new Widths())
                width += item;
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "the loop runs MoveNext to decide whether to keep going, so what it reads decides how many "
            + "times this callback's own body runs");
    }

    [Test]
    public void AStaticLambdaWhoseForEachCurrentReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeIteration(
            """
            internal sealed class Widths
            {
                public Enumerator GetEnumerator() => new Enumerator();
            }

            internal struct Enumerator
            {
                public float Current => Settings.Offset;

                public bool MoveNext() => false;
            }
            """,
            """
            foreach (float item in new Widths())
                width += item;
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "Current is the getter the loop runs to produce each element, and the element is what the "
            + "callback answers with");
    }

    [Test]
    public void AStaticLambdaWhoseForEachDisposesARefStructReadingAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeIteration(
            """
            internal sealed class Widths
            {
                public Enumerator GetEnumerator() => new Enumerator();
            }

            internal ref struct Enumerator
            {
                public float Current => 0f;

                public bool MoveNext() => false;

                public void Dispose() => _ = Settings.Offset;
            }
            """,
            """
            foreach (float item in new Widths())
                width += item;
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "a ref struct enumerator is disposed by the name alone, and the loop runs that Dispose where "
            + "it ends whether or not the body ever ran");
    }

    [Test]
    public void AStaticLambdaWhoseForEachDisposesThroughIDisposableReadingAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeIteration(
            """
            internal sealed class Widths
            {
                public Enumerator GetEnumerator() => new Enumerator();
            }

            internal sealed class Enumerator : IDisposable
            {
                public float Current => 0f;

                public bool MoveNext() => false;

                public void Dispose() => _ = Settings.Offset;
            }
            """,
            """
            foreach (float item in new Widths())
                width += item;
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "the loop names IDisposable.Dispose, which has a body nowhere, so reading the enumerator's own "
            + "implementation is the only way to see what the disposal runs");
    }

    [Test]
    public void AStaticLambdaWhoseForEachExtensionGetEnumeratorReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeIteration(
            """
            internal sealed class Widths
            {
            }

            internal static class WidthsExtensions
            {
                public static Enumerator GetEnumerator(this Widths widths)
                {
                    _ = Settings.Offset;
                    return new Enumerator();
                }
            }

            internal struct Enumerator
            {
                public float Current => 0f;

                public bool MoveNext() => false;
            }
            """,
            """
            foreach (float item in new Widths())
                width += item;
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "a sequence with no GetEnumerator of its own still runs one, and an extension picked by the "
            + "loop is the static method it is written as");
    }

    [Test]
    public void AStaticLambdaWhoseForEachEnumerableGetEnumeratorReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeIteration(
            """
            internal sealed class Widths : IEnumerable<float>
            {
                IEnumerator<float> IEnumerable<float>.GetEnumerator()
                {
                    _ = Settings.Offset;
                    return new Enumerator();
                }

                IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<float>)this).GetEnumerator();
            }

            internal sealed class Enumerator : IEnumerator<float>
            {
                public float Current => 0f;

                object IEnumerator.Current => Current;

                public bool MoveNext() => false;

                public void Reset()
                {
                }

                public void Dispose()
                {
                }
            }
            """,
            """
            foreach (float item in new Widths())
                width += item;
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "the loop names IEnumerable<float>.GetEnumerator, and the explicit implementation behind it is "
            + "the body that actually runs");
    }

    [Test]
    public void AStaticLambdaDeconstructingAForEachWhoseGetEnumeratorReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeIteration(
            """
            internal readonly struct Pair
            {
                public void Deconstruct(out float x, out float y)
                {
                    x = 0f;
                    y = 0f;
                }
            }

            internal sealed class Pairs
            {
                public Enumerator GetEnumerator()
                {
                    _ = Settings.Offset;
                    return new Enumerator();
                }
            }

            internal struct Enumerator
            {
                public Pair Current => new Pair();

                public bool MoveNext() => false;
            }
            """,
            """
            foreach (var (x, y) in new Pairs())
                width += x + y;
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "the deconstructing form runs the iteration too, so the Deconstruct it does spell is not the "
            + "whole of what it runs");
    }

    [Test]
    public void AMethodGroupWhoseAwaitForEachGetAsyncEnumeratorReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System.Threading.Tasks;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal sealed class Widths
            {
                public Enumerator GetAsyncEnumerator()
                {
                    _ = Settings.Offset;
                    return new Enumerator();
                }
            }

            internal struct Enumerator
            {
                public float Current => 0f;

                public ValueTask<bool> MoveNextAsync() => new ValueTask<bool>(false);
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(Transform, static value => value);

                private static Rect Transform(Rect value)
                {
                    _ = SumAsync();

                    return value;
                }

                private static async Task<float> SumAsync()
                {
                    float total = 0f;
                    await foreach (float item in new Widths())
                        total += item;

                    return total;
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "an await foreach asks for its enumerator exactly as a foreach does, and awaiting the answer "
            + "is not a reason the ask went unmade");
    }

    [Test]
    public void AStaticLambdaIteratingAnArray_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeIteration(
            "",
            """
            foreach (float item in new float[1])
                width += item;
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "the compiler indexes an array itself, and what it does instead of calling has no body to read");
    }

    [Test]
    public void AStaticLambdaIteratingAString_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeIteration(
            "",
            """
            foreach (char item in "ab")
                width += item;
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "a string answers with framework members, and a callee with no source here is where the walk "
            + "stops without reporting");
    }

    [Test]
    public void AStaticLambdaIteratingASpan_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeIteration(
            "",
            """
            ReadOnlySpan<float> items = new float[1];
            foreach (float item in items)
                width += item;
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "a span's enumerator is the framework's, and the conversion that made the span is too");
    }

    [Test]
    public void AStaticLambdaIteratingAList_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeIteration(
            "",
            """
            foreach (float item in new List<float>())
                width += item;
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "the whole of what a List<float> loop runs is declared in another assembly");
    }

    [Test]
    public void AStaticLambdaIteratingAPureSourceEnumerator_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeIteration(
            """
            internal sealed class Widths
            {
                public Enumerator GetEnumerator() => new Enumerator();
            }

            internal sealed class Enumerator : IDisposable
            {
                public float Current => 0f;

                public bool MoveNext() => false;

                public void Dispose()
                {
                }
            }
            """,
            """
            foreach (float item in new Widths())
                width += item;
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "all four members are here to be read and none of them reads a static, which is the answer a "
            + "rule that never looked would give as well - the reported cases beside this one are what "
            + "tell the two apart");
    }

    [Test]
    public void AStaticLambdaUsingWithWhoseCopyConstructorReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal sealed record BoundsState(float X)
            {
                private BoundsState(BoundsState original) => X = original.X + Settings.Offset;
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            BoundsState state = new BoundsState(value.X) with { };

                            return new Rect(state.X, value.Y, value.Width, value.Height);
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "the with runs that constructor once per call, and the read in it is the callback's");
    }

    [Test]
    public void AStaticLambdaUsingWithWhoseInitialiserSetterReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal sealed record BoundsState
            {
                private readonly float _x;

                public float X
                {
                    get => _x;
                    init => _x = value + Settings.Offset;
                }
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            BoundsState state = new BoundsState() with { X = value.X };

                            return new Rect(state.X, value.Y, value.Width, value.Height);
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "the setter runs where the with is written, so the static it reads is one the callback reads");
    }

    [Test]
    public void AStaticLambdaUsingWithOnARecordStructThatDeclaresACopyConstructor_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal record struct BoundsState
            {
                public float X { get; set; }

                public BoundsState(BoundsState original)
                {
                    X = original.X + Settings.Offset;
                }
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            BoundsState state = new BoundsState() with { X = 0f };

                            return new Rect(state.X, value.Y, value.Width, value.Height);
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "the with never runs that constructor, and the creation beside it is walked where it is written");
    }

    [Test]
    public void AStaticLambdaUsingWithOnAPlainRecord_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed record BoundsState(float X, float Y);

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            BoundsState state = new BoundsState(value.X, value.Y) with { Y = 0f };

                            return new Rect(state.X, state.Y, value.Width, value.Height);
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "the copy constructor and the setters are all the compiler's, and none of them has source here "
            + "to read");
    }

    [Test]
    public void AStaticLambdaWhoseObjectInitialiserSetterReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal sealed class BoundsState
            {
                private float _width;

                public float Width
                {
                    get => _width;
                    set => _width = value + Settings.Offset;
                }
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            BoundsState state = new BoundsState { Width = value.Width };

                            return new Rect(value.X, value.Y, state.Width, value.Height);
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "an initialiser member is a setter call written without a receiver, and the object being made "
            + "is the receiver it runs on");
    }

    [Test]
    public void AStaticLambdaWhoseNestedObjectInitialiserGetterReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal sealed class Inner
            {
                public float Width { get; set; }
            }

            internal sealed class BoundsState
            {
                private Inner _inner = new Inner();

                private float _read;

                public Inner Inner
                {
                    get
                    {
                        _read = Settings.Offset;
                        return _inner;
                    }

                    set => _inner = value;
                }

                public float Read => _read;
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            BoundsState state = new BoundsState { Inner = { Width = value.Width } };

                            return new Rect(value.X, value.Y, state.Read, value.Height);
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "a nested initialiser writes into what the member hands back, so the accessor it runs is the "
            + "getter and following the setter instead would read the wrong body");
    }

    [Test]
    public void AStaticLambdaWhoseNestedObjectInitialiserSetterReadsAMutableStatic_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal sealed class Inner
            {
                public float Width { get; set; }
            }

            internal sealed class BoundsState
            {
                private Inner _inner = new Inner();

                public Inner Inner
                {
                    get => _inner;
                    set
                    {
                        _inner = value;
                        _inner.Width += Settings.Offset;
                    }
                }
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            BoundsState state = new BoundsState { Inner = { Width = value.Width } };

                            return new Rect(value.X, value.Y, state.Inner.Width, value.Height);
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "that setter never runs: the nested form assigns into the object the getter returned and "
            + "replaces nothing, so a rule that read it would report a body this callback never enters");
    }

    [Test]
    public void AStaticLambdaWhosePropertyPatternGetterReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal sealed class BoundsState
            {
                public float Width => Settings.Offset;
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new BoundsState() is { Width: var width }
                            ? new Rect(value.X, value.Y, width, value.Height)
                            : value,
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "a property pattern is a getter call written without a receiver, and what the pattern is "
            + "matched against is the receiver it runs on");
    }

    /// <remarks>
    /// The control for the case above: the same spelling over a getter that reads nothing, which reports
    /// only if the walk answers to the pattern rather than to the accessor it selects.
    /// </remarks>
    [Test]
    public void AStaticLambdaWhosePropertyPatternGetterReadsNothingStatic_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal sealed class BoundsState
            {
                public float Width => 4f;
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new BoundsState() is { Width: var width }
                            ? new Rect(value.X, value.Y, width, value.Height)
                            : value,
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "the getter the pattern selects reads nothing that moves between recordings");
    }

    [Test]
    public void AStaticLambdaWhoseSwitchPropertyPatternGetterReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal sealed class BoundsState
            {
                public float Width => Settings.Offset;
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new BoundsState() switch
                        {
                            { Width: > 0f } => new Rect(value.X, value.Y, 0f, value.Height),
                            _ => value,
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "a switch spells what it matches beside the arms exactly as an is does, so the receiver the "
            + "arm's pattern reads off is as readable there");
    }

    [Test]
    public void AStaticLambdaWhoseNestedPropertyPatternGetterReadsAMutableStatic_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            internal sealed class Inner
            {
                public float Width => Settings.Offset;
            }

            internal sealed class Holder
            {
                public Inner Inner { get; } = new Inner();
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Holder() is { Inner: { Width: var width } }
                            ? new Rect(value.X, value.Y, width, value.Height)
                            : value,
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "the inner pattern reads off whatever the outer member handed back, which is not a making, so "
            + "the walk has not been shown which body that getter belongs to");
    }

    /// <remarks>
    /// The bounds argument is there so the cases prove the rule reaches the delegate parameter alone: a
    /// description's Create takes metadata and planner traits beside its callback, and none of those carry a
    /// closure to report.
    /// </remarks>
    private static ImmutableArray<Diagnostic> AnalyzeDescriptionCallback(
        string description,
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
                    => {{description}}.Create(
                        0f,
                        {{callback}},
                        RenderBoundsContract.Create(static value => value, static value => value));

                private static void Use({{session}} session, float value)
                {
                }
            }
            """);

    /// <remarks>
    /// The opt-out beside it, which has no state parameter to carry the captured value through and is not
    /// meant to.
    /// </remarks>
    private static ImmutableArray<Diagnostic> AnalyzeRequestLocalCallback(
        string description,
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
                    => {{description}}.CreateRequestLocal(
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

    [Test]
    public void AShaderBinderReadingOnlyTheDeclaringNode_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using Beutl.Graphics.Rendering;
            using Beutl.Graphics.Shaders;

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
            using Beutl.Graphics.Rendering;
            using Beutl.Graphics.Shaders;

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

    [Test]
    public void ACapturingBindingDeclarationOnAShaderDescriptionFactory_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics.Shaders;

            internal static class Author
            {
                public static void Build(float amount)
                    => ShaderDescription.CurrentPixel(
                        "half4 apply(half4 color) { return color; }",
                        bindings => bindings.Uniform(
                            "amount",
                            amount,
                            static (writer, value, context) => Consume(value)));

                private static void Consume(float value) { }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "the declaration runs while the description is being constructed and is never retained");
    }

    [Test]
    public void ACapturingBinderRegisteredByThatDeclaration_IsStillReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics.Shaders;

            internal static class Author
            {
                public static void Build(float scale)
                    => ShaderDescription.CurrentPixel(
                        "half4 apply(half4 color) { return color; }",
                        bindings => bindings.Uniform(
                            "amount",
                            1f,
                            (writer, value, context) => Consume(value * scale)));

                private static void Consume(float value) { }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG003"),
            "a retained binder is judged the way every retained callback is, wherever it was written");
    }

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

    [Test]
    public void AStaticLambdaSubscribingToANoOpCustomStaticEvent_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static event Action Changed { add { } remove { } }
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
            Is.Empty,
            "an accessor that stores nothing leaves no subscriber list for a later += to differ by");
    }

    [Test]
    public void AStaticLambdaSubscribingToACustomStaticEventWhoseAccessorWritesAStaticDelegate_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                private static Action s_handlers;

                public static event Action Changed
                {
                    add => s_handlers += value;
                    remove => s_handlers -= value;
                }
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
            "the accessor writes a mutable static delegate, which is the same subscriber list a field-like "
            + "event holds and the same hazard under a different spelling");
    }

    [Test]
    public void AStaticLambdaSubscribingToAStaticEventFromAnotherAssembly_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeWithLibrary(
            """
            using System;

            namespace Outside;

            public static class Settings
            {
                public static event Action Changed { add { } remove { } }
            }
            """,
            """
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;
            using Outside;

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
            "the accessors have no source here, so whether a subscription is stored cannot be seen, and "
            + "silence would say the rule looked when it looked at nothing");
    }

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
    /// A scope whose constructor is pure and whose <c>Dispose</c> is not, with the callback's answer taken
    /// from what the disposal wrote.
    /// </summary>
    /// <remarks>
    /// Keeping the impurity out of the constructor is what leaves the disposal as the only thing to find:
    /// a rule that followed only the creation walks this body and reports nothing.
    /// </remarks>
    private const string DisposalStubs = """
        using System;
        using Beutl.Graphics;
        using Beutl.Graphics.Rendering;

        internal sealed class Box
        {
            public float Value;
        }

        internal sealed class Scope : IDisposable
        {
            private static float s_counter;

            private readonly Box _box;

            public Scope(Box box) => _box = box;

            public void Dispose() => _box.Value = s_counter++;
        }
        """;

    [Test]
    public void AUsingStatementWhoseDisposeReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze($$"""
            {{DisposalStubs}}

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            var box = new Box();
                            using (new Scope(box))
                            {
                            }

                            return new Rect(value.X + box.Value, value.Y, value.Width, value.Height);
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "the scope's Dispose runs at the closing brace and is as much of what the callback runs as the "
            + "block it closes");
    }

    [Test]
    public void AMethodGroupWhoseUsingScopeDisposeReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze($$"""
            {{DisposalStubs}}

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(Transform, static value => value);

                private static Rect Transform(Rect value)
                {
                    var box = new Box();
                    using (new Scope(box))
                    {
                    }

                    return new Rect(value.X + box.Value, value.Y, value.Width, value.Height);
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "a method group is read as the body it names, so the disposal in that body is reached too");
    }

    [Test]
    public void AUsingDeclarationWhoseDisposeReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze($$"""
            {{DisposalStubs}}

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            var box = new Box();
                            using Scope scope = new Scope(box);
                            return new Rect(value.X + box.Value, value.Y, value.Width, value.Height);
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "a using declaration moves where Dispose runs to the enclosing brace, not whether it runs");
    }

    [Test]
    public void ANonStaticLambdaWhoseUsingScopeDisposeReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze($$"""
            {{DisposalStubs}}

            internal sealed class Author
            {
                public RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        value =>
                        {
                            var box = new Box();
                            using (new Scope(box))
                            {
                            }

                            return new Rect(value.X + box.Value, value.Y, value.Width, value.Height);
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "this rule reads a lambda's body whether or not it is static, because being static says what "
            + "the lambda closed over and nothing about what its body runs");
    }

    [Test]
    public void AStaticLambdaReadingAMutableStaticBesideAPureUsingScope_IsReportedOnce()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze($$"""
            {{DisposalStubs.Replace("_box.Value = s_counter++", "_box.Value = 1f")}}

            internal static class Settings
            {
                public static float Offset;
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            var box = new Box();
                            using (new Scope(box))
                            {
                            }

                            return new Rect(value.X + Settings.Offset, value.Y, value.Width, value.Height);
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Has.Exactly(1).EqualTo("BESG004"),
            "one mutable static read directly is one diagnostic, and a disposal that reads nothing must "
            + "not add a second");
    }

    [Test]
    public void AUsingScopeWhoseConstructorReadsAMutableStatic_IsReportedOnce()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal sealed class Box
            {
                public float Value;
            }

            internal sealed class Scope : IDisposable
            {
                private static float s_counter;

                public Scope(Box box) => box.Value = s_counter++;

                public void Dispose() { }
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            var box = new Box();
                            using (new Scope(box))
                            {
                            }

                            return new Rect(value.X + box.Value, value.Y, value.Width, value.Height);
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Has.Exactly(1).EqualTo("BESG004"),
            "the creation was already followed before the disposal was, and following the disposal too "
            + "must not report the same scope twice");
    }

    [Test]
    public void AUsingScopeAlsoDisposedExplicitly_IsReportedOnce()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze($$"""
            {{DisposalStubs}}

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            var box = new Box();
                            using Scope scope = new Scope(box);
                            scope.Dispose();
                            return new Rect(value.X + box.Value, value.Y, value.Width, value.Height);
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Has.Exactly(1).EqualTo("BESG004"),
            "the scope's Dispose is one body however many times this one is run, and the walk reports a "
            + "body once");
    }

    [Test]
    public void ACollectionExpressionWhoseBuilderReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            [CollectionBuilder(typeof(TallyBuilder), nameof(TallyBuilder.Create))]
            internal readonly struct Tally
            {
                public Tally(float total) => Total = total;

                public float Total { get; }

                public IEnumerator<float> GetEnumerator() => null;
            }

            internal static class TallyBuilder
            {
                private static float s_bias;

                public static Tally Create(ReadOnlySpan<float> values) => new Tally(s_bias);
            }

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            Tally tally = [value.X, value.Y];
                            return new Rect(tally.Total, value.Y, value.Width, value.Height);
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "the brackets name no method, and the builder they run is where the mutable static is read");
    }

    [Test]
    public void ACollectionExpressionWhoseConstructorReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze($$"""
            {{BagStubs}}

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            Bag bag = [value.X, value.Y];
                            return new Rect(bag.Total, value.Y, value.Width, value.Height);
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "a collection type with no builder is constructed by its own constructor, which the brackets "
            + "name no more than they name a builder");
    }

    [Test]
    public void AnObjectCreationWhoseCollectionConstructorReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze($$"""
            {{BagStubs}}

            internal static class Author
            {
                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            var bag = new Bag { value.X, value.Y };
                            return new Rect(bag.Total, value.Y, value.Width, value.Height);
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "the same constructor spelled as an object creation is the control the collection expression "
            + "is measured against");
    }

    /// <summary>A collection type built by its own constructor, which reads a mutable static.</summary>
    private const string BagStubs = """
        using System.Collections;
        using System.Collections.Generic;
        using Beutl.Graphics;
        using Beutl.Graphics.Rendering;

        internal sealed class Bag : IEnumerable<float>
        {
            private static float s_bias;

            public Bag() => Total = s_bias;

            public float Total { get; }

            public void Add(float value) { }

            public IEnumerator<float> GetEnumerator() => null;

            IEnumerator IEnumerable.GetEnumerator() => null;
        }
        """;

    [Test]
    public void AnInterpolatedStringWhoseHandlerConstructorReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeInterpolatedStringHandler("""
            public BoundsHandler(int literalLength, int formattedCount) => _total = Settings.Offset;

            public void AppendLiteral(string text) { }

            public void AppendFormatted<T>(T formatted) { }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "the handler is made by the compiler from the type the argument is used as, so the string "
            + "spells a constructor call that has no name anywhere in it");
    }

    [Test]
    public void AnInterpolatedStringWhoseHandlerAppendReadsAMutableStatic_IsReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeInterpolatedStringHandler("""
            public BoundsHandler(int literalLength, int formattedCount) => _total = 0f;

            public void AppendLiteral(string text) { }

            public void AppendFormatted<T>(T formatted) => _total += Settings.Offset;
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "each hole in the string is an AppendFormatted call the string does not name either");
    }

    /// <remarks>
    /// The control for the two above: the same handler with nothing static behind any of its members,
    /// which reports only if the walk answers to the string rather than to the bodies it runs.
    /// </remarks>
    [Test]
    public void AnInterpolatedStringWhoseHandlerReadsNothingStatic_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = AnalyzeInterpolatedStringHandler("""
            public BoundsHandler(int literalLength, int formattedCount) => _total = 0f;

            public void AppendLiteral(string text) { }

            public void AppendFormatted<T>(T formatted) => _total += 1f;
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "a handler that reads nothing that moves between recordings answers the same way twice");
    }

    /// <remarks>
    /// A handler is the shape that spells none of the members it runs: the compiler picks the constructor
    /// and the appends off the parameter's type and fills them from the string's own parts.
    /// </remarks>
    private static ImmutableArray<Diagnostic> AnalyzeInterpolatedStringHandler(string members)
        => Analyze($$"""
            using System.Runtime.CompilerServices;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Settings
            {
                public static float Offset;
            }

            [InterpolatedStringHandler]
            internal ref struct BoundsHandler
            {
                private float _total;

                {{members}}

                public readonly float Total => _total;
            }

            internal static class Author
            {
                private static float Measure(BoundsHandler handler) => handler.Total;

                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value => new Rect(
                            Measure($"x{value.Y}"), value.Y, value.Width, value.Height),
                        static value => value);
            }
            """);

    [Test]
    public void AConditionalHelperReadingAMutableStatic_IsReportedWhenTheSymbolIsDefined()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze(
            RecordingCallback("""[Conditional("DEBUG")]"""),
            parseOptions: CSharpParseOptions.Default.WithPreprocessorSymbols("DEBUG"));

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Does.Contain("BESG004"),
            "DEBUG is defined, so the call is compiled and what the helper reads is what the callback reads");
    }

    [Test]
    public void AConditionalHelperReadingAMutableStatic_IsNotReportedWhenTheSymbolIsNotDefined()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze(RecordingCallback("""[Conditional("DEBUG")]"""));

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "the compiler removes the call, and a read in a body no shipped build runs is not a read the "
            + "callback makes");
    }

    [Test]
    public void ANonConditionalHelperReadingAMutableStatic_IsReportedEitherWay()
    {
        string source = RecordingCallback(string.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(
                Analyze(source).Select(static d => d.Id),
                Does.Contain("BESG004"),
                "no attribute means no omission, so the preprocessor symbols decide nothing here");
            Assert.That(
                Analyze(source, parseOptions: CSharpParseOptions.Default.WithPreprocessorSymbols("DEBUG"))
                    .Select(static d => d.Id),
                Does.Contain("BESG004"),
                "and defining DEBUG changes nothing either");
        });
    }

    /// <summary>A callback whose only read of a mutable static is inside a helper carrying <paramref name="attribute"/>.</summary>
    private static string RecordingCallback(string attribute) => $$"""
        using System.Diagnostics;
        using Beutl.Graphics;
        using Beutl.Graphics.Rendering;

        internal static class Author
        {
            private static float s_offset;

            private static float s_recorded;

            {{attribute}}
            private static void Record() => s_recorded = s_offset;

            public static RenderBoundsContract Build()
                => RenderBoundsContract.Create(
                    static value =>
                    {
                        Record();
                        return value;
                    },
                    static value => value);
        }
        """;

    [Test]
    public void ADebugAssertReadingAMutableStatic_IsReportedOnlyWhereItIsCompiled()
    {
        string source = """
            using System.Diagnostics;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Author
            {
                private static float s_offset;

                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            Debug.Assert(s_offset >= 0f);
                            return value;
                        },
                        static value => value);
            }
            """;

        Assert.Multiple(() =>
        {
            Assert.That(
                Analyze(source).Select(static d => d.Id),
                Is.Empty,
                "the whole call goes, arguments included, so the read is in no shipped build");
            Assert.That(
                Analyze(source, parseOptions: CSharpParseOptions.Default.WithPreprocessorSymbols("DEBUG"))
                    .Select(static d => d.Id),
                Does.Contain("BESG004"),
                "with DEBUG the assert is compiled and the callback really does read the static");
        });
    }

    [Test]
    public void AConditionalHelperArgumentReadingAMutableStatic_IsNotReportedWhenTheSymbolIsNotDefined()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze("""
            using System.Diagnostics;
            using Beutl.Graphics;
            using Beutl.Graphics.Rendering;

            internal static class Author
            {
                private static float s_offset;

                [Conditional("DEBUG")]
                private static void Record(float value) { }

                public static RenderBoundsContract Build()
                    => RenderBoundsContract.Create(
                        static value =>
                        {
                            Record(s_offset);
                            return value;
                        },
                        static value => value);
            }
            """);

        Assert.That(
            diagnostics.Select(static d => d.Id),
            Is.Empty,
            "a removed call evaluates none of its arguments, so a read written as one is not a read the "
            + "callback makes");
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

    /// <summary>Runs the analyzer over <paramref name="source"/> parsed with <paramref name="parseOptions"/>.</summary>
    /// <remarks>
    /// The parse options are a parameter for the sake of the preprocessor symbols: a <c>[Conditional]</c>
    /// call is kept or dropped by the symbols defined where it is written, so a harness that could not vary
    /// them could not tell a callee the build keeps from one it removes.
    /// </remarks>
    private static ImmutableArray<Diagnostic> Analyze(
        string source,
        MetadataReference? library = null,
        CSharpParseOptions? parseOptions = null)
    {
        parseOptions ??= CSharpParseOptions.Default;

        CSharpCompilation compilation = CSharpCompilation.Create(
            "AnalyzerTest",
            library is null
                ?
                [
                    CSharpSyntaxTree.ParseText(ContractStubs, parseOptions),
                    CSharpSyntaxTree.ParseText(source, parseOptions),
                ]
                : [CSharpSyntaxTree.ParseText(source, parseOptions)],
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
