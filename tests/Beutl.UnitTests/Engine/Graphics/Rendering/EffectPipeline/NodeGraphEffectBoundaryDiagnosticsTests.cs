using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Logging;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Pins the render-node boundary contract for <see cref="NodeGraphFilterEffect"/>: effects hosted INSIDE a node
/// graph must share the owning renderer's <see cref="PipelineDiagnostics"/> and <see cref="RenderTargetPool"/>,
/// not a private per-node instance. Regression guard for the boundary drop where
/// <c>NodeGraphFilterEffectRenderNode</c> built its inner <see cref="RenderNodeProcessor"/> without threading the
/// parent context's diagnostics/pool, so inner effect activity was invisible to <c>IRenderer.Diagnostics</c>
/// (FR-017 under-report) and bypassed the shared pool (FR-006 steady-state pooling did not apply there).
/// </summary>
[NonParallelizable]
[TestFixture]
public class NodeGraphEffectBoundaryDiagnosticsTests
{
    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
    }

    // (a) The parent renderer's diagnostics MUST observe the inner effect's activity, and (b) a second identical
    // frame driven through the same pool MUST reach steady-state reuse (no fresh allocations / misses, still one
    // acquire — the same buffer, now a hit). Before the boundary fix the inner processor owned a throwaway
    // diagnostics and a null pool, so every parent counter here read zero on every frame.
    [Test]
    public void NodeGraphHostedEffect_SharesParentDiagnosticsAndPool()
    {
        const int frames = 4;
        PipelineDiagnosticsSnapshot[] perFrame = RenderFramesWithPool(MakeNodeGraphGammaHost, frames);

        Assert.Multiple(() =>
        {
            // (a) frame 1: the inner Gamma pass is counted on the parent diagnostics and warms the shared pool.
            Assert.That(perFrame[0].GpuPasses, Is.GreaterThanOrEqualTo(1),
                "the node-graph-hosted effect's GPU pass is counted on the parent diagnostics");
            Assert.That(perFrame[0].PoolAcquires, Is.GreaterThanOrEqualTo(1),
                "the inner effect acquires its intermediate from the shared pool");
            Assert.That(perFrame[0].PoolMisses, Is.GreaterThanOrEqualTo(1),
                "frame 1 warms the shared pool with the inner effect's intermediate");
            Assert.That(perFrame[0].TargetAllocations, Is.EqualTo(perFrame[0].PoolMisses),
                "each miss allocates exactly one target");

            // (b) frames 2..K: the same buffer is reused — still acquired (through the shared pool) but now a hit.
            for (int f = 1; f < frames; f++)
            {
                Assert.That(perFrame[f].PoolAcquires, Is.GreaterThanOrEqualTo(1),
                    $"frame {f + 1} still acquires the inner effect's buffer through the shared pool");
                Assert.That(perFrame[f].TargetAllocations, Is.EqualTo(0),
                    $"frame {f + 1} adds no fresh allocations (steady-state reuse)");
                Assert.That(perFrame[f].PoolMisses, Is.EqualTo(0),
                    $"frame {f + 1} has no pool misses (the warmed buffer is reused)");
            }
        });
    }

    // Renders the same node-graph scene `frames` times through one shared RenderTargetPool + PipelineDiagnostics,
    // mirroring how Renderer drives per-frame processors over a persistent pool (Trim at frame start, fresh
    // node/processor per frame, ops disposed within the frame so leases return to the pool). Counters are reset
    // per frame so each snapshot is that frame's delta. This is the same driver shape as EffectPipelineCounterTests.
    private static PipelineDiagnosticsSnapshot[] RenderFramesWithPool(Func<Drawable.Resource> makeScene, int frames)
    {
        VulkanTestEnvironment.EnsureAvailable();
        return VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            PixelSize size = SceneFixtures.ReferenceSize;
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();
            var snapshots = new PipelineDiagnosticsSnapshot[frames];

            for (int f = 0; f < frames; f++)
            {
                pool.Trim(f);
                diagnostics.Reset();

                using RenderTarget target = RenderTarget.Create(size.Width, size.Height)
                                            ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
                using var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1f, logicalSize: size.ToSize(1));
                canvas.Clear(Colors.Black);

                Drawable.Resource resource = makeScene();
                using var node = new DrawableRenderNode(resource);
                using (var ctx = new GraphicsContext2D(node, size.ToSize(1), 1f))
                {
                    resource.GetOriginal().Render(ctx, resource);
                }

                var processor = new RenderNodeProcessor(
                    pool, node, useRenderCache: false, RenderIntent.Delivery, outputScale: 1f,
                    diagnostics: diagnostics);
                RenderNodeOperation[] ops = processor.PullToRoot();
                foreach (RenderNodeOperation op in ops)
                {
                    op.Render(canvas);
                    op.Dispose();
                }

                snapshots[f] = diagnostics.Snapshot();
            }

            return snapshots;
        });
    }

    // A host shape whose FilterEffect is a NodeGraph: Input -> FilterEffectNode<Gamma> -> Output. Gamma is a
    // coordinate-invariant color effect, so it compiles to a single fused pass that acquires one pooled
    // intermediate — the minimal case that exercises the boundary counter/pool threading.
    private static Drawable.Resource MakeNodeGraphGammaHost()
    {
        var effect = new NodeGraphFilterEffect();
        GraphModel model = effect.Model.CurrentValue!;

        var inputNode = new FilterEffectInputNode();
        var gammaNode = new FilterEffectNode<Gamma>();
        gammaNode.Object.Amount.CurrentValue = 1.5f;
        var outputNode = new OutputNode();
        model.Nodes.Add(inputNode);
        model.Nodes.Add(gammaNode);
        model.Nodes.Add(outputNode);

        var chainInput = (IInputPort)gammaNode.Items[1];
        var chainOutput = (IOutputPort)gammaNode.Items[0];
        model.Connect(chainInput, inputNode.Output);
        model.Connect(outputNode.InputPort, chainOutput);

        return MakeShape(effect);
    }

    private static Drawable.Resource MakeShape(FilterEffect effect)
    {
        var fill = new LinearGradientBrush();
        fill.StartPoint.CurrentValue = new RelativePoint(0, 0, RelativeUnit.Relative);
        fill.EndPoint.CurrentValue = new RelativePoint(1, 1, RelativeUnit.Relative);
        fill.GradientStops.Add(new GradientStop(Colors.Red, 0));
        fill.GradientStops.Add(new GradientStop(Colors.Lime, 0.5f));
        fill.GradientStops.Add(new GradientStop(Colors.Blue, 1));

        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        shape.Width.CurrentValue = 240;
        shape.Height.CurrentValue = 150;
        shape.Fill.CurrentValue = fill;

        var rotation = new RotationTransform();
        rotation.Rotation.CurrentValue = 12f;
        shape.Transform.CurrentValue = rotation;

        shape.FilterEffect.CurrentValue = effect;
        return shape.ToResource(CompositionContext.Default);
    }
}
