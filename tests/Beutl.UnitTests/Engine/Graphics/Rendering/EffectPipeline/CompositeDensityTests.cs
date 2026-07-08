using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Pins the composite fan-in density contract (review round 3, M1): a <see cref="CompositePass"/> that fans in
/// INDEPENDENT inputs of mixed supply density (a supported 003 FR-019 scene) must buffer at the boundary working
/// scale clamped to the union, NOT the minimum of the inputs' densities. Folding to the min silently downsamples the
/// higher-density layer, violating C3.2 "same resulting densities as pre-redesign" (legacy composited at the boundary
/// <c>WorkingScale</c>, never the min of the input targets' scales).
/// </summary>
[NonParallelizable]
[TestFixture]
public class CompositeDensityTests
{
    private static readonly Rect s_bounds = new(0, 0, 160, 120);

    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
    }

    // A composite over a 2x-density layer beside a 1x-density layer must produce a 2x output (boundary working scale
    // clamped to the union), not a 1x output (the min of the inputs). The min-carry is correct for single-op
    // re-materialization and split branches (which share one scale) but wrong for the fan-in of independent inputs.
    [Test]
    public void Composite_FanIn_BuffersAtBoundaryScale_NotMinOfInputs()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            const float boundary = 2f;
            var builder = new EffectGraphBuilder(s_bounds, outputScale: boundary, workingScale: boundary);
            builder.Composite(CompositeNodeDescriptor.Create(BlendMode.SrcOver, structuralToken: "m1-composite"));

            using EffectGraph graph = builder.Build();
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();
            CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics);
            FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, boundary);

            RenderNodeOperation[] outputs = PlanExecutor.Execute(
                plan, frame, [OpAtScale(2f), OpAtScale(1f)], outputScale: boundary, workingScale: boundary,
                maxWorkingScale: float.PositiveInfinity, diagnostics: diagnostics, pool: pool);

            float density = outputs[0].EffectiveScale.Value;
            RenderNodeOperation.DisposeAll(outputs);
            Assert.That(density, Is.EqualTo(boundary),
                "the composite buffers at the boundary working scale clamped to the union, not the min input density");
        });
    }

    private static RenderNodeOperation OpAtScale(float scale)
        => RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas => canvas.DrawRectangle(s_bounds.Deflate(16), Brushes.Resource.White, null),
            hitTest: s_bounds.Contains,
            effectiveScale: EffectiveScale.At(scale));
}
