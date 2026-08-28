using System.Reflection;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>
/// Pins what an execution callback may be written against - the node that declares it - and what that costs
/// the plan key.
/// </summary>
/// <remarks>
/// A metadata callback contributes which declaration it is and nothing about the instance it reads, because
/// the engine holds it to being a pure function of its arguments. An execution callback carries no such
/// promise, so what it closed over still separates it; the node it was written inside is the one target
/// that is not something it closed over, and it is therefore the one that does not.
/// </remarks>
[TestFixture]
public sealed class NodeCapturingExecutionCallbackTests
{
    private const string ScalingSource =
        """
        uniform float amount;

        half4 apply(half4 color) {
            return color * amount;
        }
        """;

    private static readonly Rect s_domain = new(0, 0, 200, 100);
    private static readonly Rect s_sourceBounds = new(0, 0, 40, 20);

    [Test]
    public void ANodeDrawingFromItsOwnProperty_RecordsAndRasterizes()
    {
        using var node = new PaintingNode(200);
        using RenderNodeRenderer renderer = CreateRenderer(node);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.Bitmap, Is.Not.Null);
            Assert.That(
                rasterization.Bitmap!.GetPixelSpan<ushort>().ToArray(),
                Has.Some.Not.Zero,
                "the drawing must have answered from the node's own property");
        });
    }

    /// <remarks>
    /// The half that says a shared plan is shape and not content. A painted source's plan key never held
    /// its drawing, so these two nodes shared a plan before this change as well; what has to hold is that
    /// the shared plan is re-run over each node rather than replayed from the first.
    /// </remarks>
    [Test]
    public void TwoNodesOfOneTypeDrawingDifferentValues_ShareOnePlanAndRenderTheirOwn()
    {
        using var root = new ContainerRenderNode();
        root.AddChild(new PaintingNode(200));
        using RenderNodeRenderer renderer = CreateRenderer(root);

        ushort[] first = ReadPixels(renderer);
        long afterFirstNode = renderer.StructuralPlanCacheStatistics.Compilations;
        root.SetChild(0, new PaintingNode(80));
        ushort[] second = ReadPixels(renderer);

        Assert.Multiple(() =>
        {
            Assert.That(afterFirstNode, Is.EqualTo(1));
            Assert.That(renderer.StructuralPlanCacheStatistics.Compilations, Is.EqualTo(1));
            Assert.That(first, Has.Some.Not.Zero);
            Assert.That(
                second,
                Is.Not.EqualTo(first),
                "the shared plan must be re-run over the second node's own value, not replayed from the "
                + "first node's");
        });
    }

    [Test]
    public void ANodeBindingAShaderUniformFromItsOwnProperty_RecordsAndRasterizes()
    {
        using var node = new ScalingNode(0.5f);
        using RenderNodeRenderer renderer = CreateRenderer(node);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.Bitmap, Is.Not.Null);
            Assert.That(rasterization.Bitmap!.GetPixelSpan<ushort>().ToArray(), Has.Some.Not.Zero);
        });
    }

    /// <remarks>
    /// The reason an execution callback's plan key had to stop being its delegate. A shader binder that
    /// reads its own node is a different delegate per node, so before this change two nodes of one type
    /// compiled a plan each and hit the cache never.
    /// </remarks>
    [Test]
    public void TwoNodesOfOneTypeBindingDifferentValues_CompileOneStructuralPlan()
    {
        using var root = new ContainerRenderNode();
        root.AddChild(new ScalingNode(0.5f));
        using RenderNodeRenderer renderer = CreateRenderer(root);

        renderer.Rasterize().Dispose();
        long afterFirstNode = renderer.StructuralPlanCacheStatistics.Compilations;
        root.SetChild(0, new ScalingNode(0.25f));
        renderer.Rasterize().Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(afterFirstNode, Is.EqualTo(1));
            Assert.That(
                renderer.StructuralPlanCacheStatistics.Compilations,
                Is.EqualTo(1),
                "what an execution callback reads off its own node is request data; a second node of the "
                + "same type must re-run the compiled plan rather than compile a second one");
            Assert.That(renderer.StructuralPlanCacheStatistics.Hits, Is.GreaterThan(0));
        });
    }

    /// <remarks>
    /// What the node exemption must not take with it. The request-local overloads exist so a callback may
    /// close over a recording, and the fresh identity that bars its output from a later request's cache
    /// lookup is nothing but its delegate: a closure over anything besides the node arrives as a compiler
    /// display class allocated again every recording.
    /// </remarks>
    [Test]
    public void ACapturingRequestLocalCallback_StillTakesAFreshIdentityPerRecording()
    {
        using var root = new ContainerRenderNode();
        root.AddChild(new RequestLocalNode(new Rect(0, 0, 10, 10)));
        using RenderNodeRenderer renderer = CreateRenderer(root);

        renderer.Rasterize().Dispose();
        long afterFirstNode = renderer.StructuralPlanCacheStatistics.Compilations;
        root.SetChild(0, new RequestLocalNode(new Rect(0, 0, 20, 20)));
        renderer.Rasterize().Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(afterFirstNode, Is.EqualTo(1));
            Assert.That(
                renderer.StructuralPlanCacheStatistics.Compilations,
                Is.EqualTo(2),
                "a request-local callback closes over a recording, so it must keep compiling a plan of "
                + "its own rather than inherit the sharing a node-bound callback earns");
        });
    }

    /// <remarks>
    /// A change to state only the drawing reads has to reach the next frame, and what makes it do so is
    /// the mark: a node reporting no change may have its recording replayed instead of re-recorded, which
    /// is the contract BESG005 reports an unmarked write against.
    /// </remarks>
    [Test]
    public void AMarkedChangeToAValueOnlyTheDrawingReads_ReachesTheNextFrame()
    {
        using var node = new PaintingNode(200);
        using RenderNodeRenderer renderer = CreateRenderer(node);

        ushort[] before = ReadPixels(renderer);
        node.SetLevel(80);
        ushort[] after = ReadPixels(renderer);

        Assert.Multiple(() =>
        {
            Assert.That(before, Has.Some.Not.Zero);
            Assert.That(after, Is.Not.EqualTo(before));
        });
    }

    [Test]
    public void StructuralIdentityOfExecution_KeysTheDeclaringNodeByMethodAndEverythingElseByDelegate()
    {
        using var first = new PaintingNode(1);
        using var second = new PaintingNode(2);
        int captured = 0;
        Action closesOverALocal = () => captured++;

        object boundToFirst = RenderDescriptionValidation.StructuralIdentityOfExecution(first.CreateDrawing());
        object boundToSecond = RenderDescriptionValidation.StructuralIdentityOfExecution(second.CreateDrawing());
        object otherDeclaration =
            RenderDescriptionValidation.StructuralIdentityOfExecution(first.CreateOtherDrawing());
        object aClosure = RenderDescriptionValidation.StructuralIdentityOfExecution(closesOverALocal);
        object aStatic = RenderDescriptionValidation.StructuralIdentityOfExecution(NoOp);

        Assert.Multiple(() =>
        {
            Assert.That(boundToFirst, Is.InstanceOf<MethodInfo>());
            Assert.That(boundToSecond, Is.EqualTo(boundToFirst),
                "two nodes of one type declare one execution callback and share the shape of its work");
            Assert.That(otherDeclaration, Is.Not.EqualTo(boundToFirst),
                "two declarations stay two identities");
            Assert.That(aClosure, Is.SameAs(closesOverALocal),
                "a display class is what it closed over, so the delegate has to keep separating it");
            Assert.That(aStatic, Is.SameAs((Delegate)NoOp),
                "a static callback is already one cached delegate per declaration");
        });
    }

    private static void NoOp()
    {
    }

    private static ushort[] ReadPixels(RenderNodeRenderer renderer)
    {
        using RenderNodeRasterization rasterization = renderer.Rasterize();
        return rasterization.Bitmap!.GetPixelSpan<ushort>().ToArray();
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode root)
        => new(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    TargetDomain = s_domain,
                    CacheOptions = RenderCacheOptions.Disabled,
                    Purpose = RenderRequestPurpose.Frame,
                },
                TargetFactory = new CpuTargetFactory(),
            });

    /// <summary>A source whose drawing reads the level the node holds.</summary>
    private sealed class PaintingNode(byte level) : RenderNode
    {
        public byte Level { get; private set; } = level;

        public void SetLevel(byte value)
        {
            Level = value;
            MarkChanged();
        }

        public Action CreateDrawing() => () => Consume(Level);

        public Action CreateOtherDrawing() => () => Consume((byte)(Level + 1));

        public override void Process(RenderNodeContext context)
            => context.Publish(context.PaintedSource(
                0,
                draw: (canvas, _, _, _) => canvas.Clear(Color.FromArgb(255, Level, 0, 0)),
                fill: null,
                pen: null,
                outputBounds: s_sourceBounds,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Vector));

        private static void Consume(byte value)
        {
        }
    }

    /// <summary>A shader stage whose uniform binder reads the scale the node holds.</summary>
    private sealed class ScalingNode(float scale) : RenderNode
    {
        public float Scale { get; } = scale;

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(OpaqueRenderDescription.CreateRequestLocal(
                static session =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(static canvas => canvas.Clear(Colors.White));
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Source(s_sourceBounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale));

            ShaderDefinition<int> definition = ShaderDefinition<int>.CurrentPixel(
                ScalingSource,
                builder => builder.Uniform<float>(
                    "amount",
                    static _ => 1f,
                    (writer, value, _) => writer.Set(value * Scale)));

            context.Publish(context.Shader(source, definition.Call(0)));
        }
    }

    /// <summary>A source whose callback closes over a local rather than over its node.</summary>
    private sealed class RequestLocalNode(Rect bounds) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            Rect local = bounds;
            context.Publish(context.OpaqueSource(OpaqueRenderDescription.CreateRequestLocal(
                session =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(canvas => canvas.Clear(Color.FromArgb(255, (byte)local.Width, 0, 0)));
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Source(local),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale)));
        }
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
            => new CpuRenderTarget(allocation.DeviceSize.Width, allocation.DeviceSize.Height);
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            width,
            height);
}
