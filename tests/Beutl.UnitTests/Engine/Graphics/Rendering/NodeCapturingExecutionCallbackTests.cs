using System.Reflection;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

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

            ShaderDescription description = ShaderDescription.CurrentPixel(
                ScalingSource,
                builder => builder.Uniform<float>(
                    "amount",
                    1f,
                    (writer, value, _) => writer.Set(value * Scale)));

            context.Publish(context.Shader(source, description));
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
