using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression cover for finding F2 (feature 004): a non-invariant SKSL script that samples non-locally must
/// declare <see cref="BoundsContract.FullFrame"/>, not identity, so a downstream deflating pass (a fixed
/// <see cref="Clipping"/>) cannot ROI-crop its bake and shift/clip the non-local samples (contract A3).
/// </summary>
[NonParallelizable]
[TestFixture]
public class SkslFullFrameBoundsTests
{
    private static readonly Rect s_bounds = new(0, 0, 160, 120);

    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
    }

    // A non-invariant SKSL script that samples with an offset, followed by a fixed (deflating) Clipping. The
    // backward-ROI walk would crop the script pass to the clip's deflated sub-rect if it declared identity bounds;
    // FullFrame keeps it baking full-frame. Assert the script pass resolves to the full input bounds.
    [Test]
    public void OffsetSamplingScriptUnderDeflatingClip_ResolvesFullFrame_NotDeflatedRoi()
    {
        FrameResources frame = ResolveChain(MakeOffsetScript(), MakeClip());

        Assert.That(frame.Passes[0].OutputRoi, Is.EqualTo(s_bounds),
            "the non-invariant SKSL pass bakes at full input bounds even under a downstream deflating clip");
    }

    // Visible symptom: an offset-sampling script over an OPAQUE source, then a fixed clip. Pre-fix the ROI crop
    // shifts the script's sampling so the clipped interior loses coverage; post-fix it stays fully covered.
    // Reference coverage is the same clip over the same source without the script.
    [Test]
    public void OffsetSamplingScriptThenClip_ClippedRegionStaysFullyCovered()
    {
        int scriptCovered = RenderAndCountOpaque(MakeOffsetScript(), MakeClip());
        int clipOnlyCovered = RenderAndCountOpaque(MakeClip());

        TestContext.WriteLine($"script+clip opaque px = {scriptCovered}, clip-only opaque px = {clipOnlyCovered}");
        Assert.That(scriptCovered, Is.GreaterThanOrEqualTo((int)(clipOnlyCovered * 0.98)),
            "the offset-script clip region stays as covered as the un-scripted clip (no ROI-crop-shifted holes)");
    }

    private static SKSLScriptEffect MakeOffsetScript()
    {
        // Samples the source 8 px to the right of the current pixel: a non-local (non-invariant) sample.
        var effect = new SKSLScriptEffect();
        effect.Script.CurrentValue =
            """
            uniform shader src;
            half4 main(float2 c) {
                return src.eval(c + float2(8.0, 0.0));
            }
            """;
        return effect;
    }

    private static Clipping MakeClip() => new()
    {
        Left = { CurrentValue = 24 },
        Top = { CurrentValue = 24 },
        Right = { CurrentValue = 24 },
        Bottom = { CurrentValue = 24 },
    };

    private static FrameResources ResolveChain(params FilterEffect[] effects)
    {
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        foreach (FilterEffect effect in effects)
            effect.Describe(builder, (FilterEffect.Resource)(object)effect.ToResource(CompositionContext.Default));

        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        return EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);
    }

    private static int RenderAndCountOpaque(params FilterEffect[] effects)
    {
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        foreach (FilterEffect effect in effects)
            effect.Describe(builder, (FilterEffect.Resource)(object)effect.ToResource(CompositionContext.Default));

        using EffectGraph graph = builder.Build();
        using var pool = new RenderTargetPool();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);
        RenderNodeOperation[] ops = PlanExecutor.Execute(
            plan, frame, [OpaqueInput()], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool, renderIntent: RenderIntent.Delivery);

        int w = (int)s_bounds.Width, h = (int)s_bounds.Height;
        using RenderTarget target = RenderTarget.Create(w, h)!;
        using (var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1f, logicalSize: s_bounds.Size))
        {
            canvas.Clear(Colors.Black);
            foreach (RenderNodeOperation op in ops)
                op.Render(canvas);
        }

        RenderNodeOperation.DisposeAll(ops);
        using Bitmap bmp = target.Snapshot();
        int opaque = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (bmp.SKBitmap.GetPixel(x, y).Red > 40)
                    opaque++;
            }
        }

        return opaque;
    }

    private static RenderNodeOperation OpaqueInput()
        => RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas => canvas.DrawRectangle(s_bounds, Brushes.Resource.White, null),
            hitTest: s_bounds.Contains);
}
