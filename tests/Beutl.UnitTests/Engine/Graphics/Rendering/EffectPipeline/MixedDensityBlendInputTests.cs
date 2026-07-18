using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression cover for the mixed-density device-space blit in <see cref="BlendEffect"/> and <see cref="InnerShadow"/>:
/// both drew their input at 1:1 device pixels, ignoring <see cref="EffectInput.Density"/>. When a forward-inflated /
/// over-budget input is carried in below the output density (input density 0.5, output density 1.0), the source
/// occupied only a fraction of the output buffer before the effect's blend, under-filling the frame. The fix wraps the
/// blit in the FlatShadow/Clipping density scale (input density -> output density). The parity anchor is the same effect
/// fed a matched-density input: the two must fill the buffer identically.
/// </summary>
[NonParallelizable]
[TestFixture]
public class MixedDensityBlendInputTests
{
    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
    }

    private static readonly Rect s_inputBounds = new(0, 0, 100, 100);

    // A non-solid (gradient) brush drives BlendEffect's GeometryNode path. With the default SrcIn blend the gradient is
    // masked by the input's coverage, so an under-filled 0.5-density input leaves the far corner transparent; a matched
    // 1.0-density input (and the fixed low-density render) fills it.
    [Test]
    public void BlendEffect_InputDensityBelowOutputDensity_FillsBufferLikeMatchedDensity()
    {
        var gradient = new LinearGradientBrush();
        gradient.GradientStops.Add(new GradientStop(Colors.Red, 0));
        gradient.GradientStops.Add(new GradientStop(Colors.Blue, 1));
        var blend = new BlendEffect { Brush = { CurrentValue = gradient } };

        AssertFarCornerMatchesMatchedDensity(blend);
    }

    // InnerShadow's DstATop composite occupies the input rect; an unscaled 0.5-density input under-fills the buffer, so
    // the far corner is transparent pre-fix. A matched-density input fills it.
    [Test]
    public void InnerShadow_InputDensityBelowOutputDensity_FillsBufferLikeMatchedDensity()
    {
        var shadow = new InnerShadow
        {
            Sigma = { CurrentValue = new Size(2, 2) },
            Color = { CurrentValue = Colors.Black },
        };

        AssertFarCornerMatchesMatchedDensity(shadow);
    }

    private static void AssertFarCornerMatchesMatchedDensity(FilterEffect effect)
    {
        // Opaque white everywhere, so the input's coverage (not its colour) is the size signal the far corner reads.
        Action<ImmediateCanvas> draw = canvas => canvas.DrawRectangle(s_inputBounds, Brushes.Resource.White, null);

        using Bitmap matched = Render(effect, draw, EffectiveScale.At(1f));
        using Bitmap lowDensity = Render(effect, draw, EffectiveScale.At(0.5f));

        byte matchedCorner = matched.SKBitmap.GetPixel(matched.Width - 4, matched.Height - 4).Alpha;
        byte lowCorner = lowDensity.SKBitmap.GetPixel(lowDensity.Width - 4, lowDensity.Height - 4).Alpha;
        TestContext.WriteLine($"far-corner alpha: matched={matchedCorner}, lowDensity={lowCorner}");

        Assert.That(matchedCorner, Is.GreaterThan(200),
            "sanity: the matched-density input fills the whole output buffer");
        Assert.That(lowCorner, Is.EqualTo(matchedCorner).Within(8),
            "the 0.5-density input under-filled the buffer: it was blitted unscaled onto the 1.0-density output "
            + $"instead of being scaled to the output density (matched corner alpha {matchedCorner}, observed {lowCorner}).");
    }

    // Drives a single filter effect over an input op of the given supply density, resolving the output at the full
    // budget (output density 1.0) so the input's lower density is the only density seam, then rasterizes to logical.
    private static Bitmap Render(FilterEffect effect, Action<ImmediateCanvas> draw, EffectiveScale inputDensity)
    {
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            s_inputBounds, draw, hitTest: s_inputBounds.Contains, onDispose: null, effectiveScale: inputDensity);

        var builder = new EffectGraphBuilder(s_inputBounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        effect.Describe(builder, (FilterEffect.Resource)(object)effect.ToResource(Composition.CompositionContext.Default));

        using EffectGraph graph = builder.Build();
        using var pool = new RenderTargetPool();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);
        RenderNodeOperation[] ops = PlanExecutor.Execute(
            plan, frame, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool, renderIntent: RenderIntent.Delivery);
        try
        {
            Rect outBounds = ops[0].Bounds;
            var size = PixelRect.FromRect(outBounds);
            using RenderTarget target = RenderTarget.Create(size.Width, size.Height)!;
            using (var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1f, logicalSize: outBounds.Size))
            {
                canvas.Clear();
                using (canvas.PushTransform(Matrix.CreateTranslation(-outBounds.X, -outBounds.Y)))
                {
                    foreach (RenderNodeOperation op in ops)
                        op.Render(canvas);
                }
            }

            return target.Snapshot();
        }
        finally
        {
            RenderNodeOperation.DisposeAll(ops);
        }
    }
}
