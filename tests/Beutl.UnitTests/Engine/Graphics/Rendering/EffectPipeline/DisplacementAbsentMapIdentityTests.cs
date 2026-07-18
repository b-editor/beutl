using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression for the signed-displacement absent-map fallback (feature 004, D7). When the displacement-map child
/// resolves to null at render time (a preview buffer allocation failure), the old code bound a transparent fallback
/// shader; <c>getDisplacement</c> read that as <c>0</c>, and the signed remap <c>d = d * 2 - 1</c> turned it into a full
/// NEGATIVE displacement instead of identity. The map's presence is now reported through the <c>uMapPresent</c> uniform,
/// and an absent map passes the source through unchanged regardless of the Signed/channel mode. The render-time null is
/// forced deterministically via the effect's test seam.
/// </summary>
[NonParallelizable]
[TestFixture]
public class DisplacementAbsentMapIdentityTests
{
    private static readonly Rect s_bounds = new(0, 0, 160, 120);
    private const int WinX0 = 10, WinY0 = 10, WinX1 = 150, WinY1 = 110;

    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory ??= Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
        VulkanTestEnvironment.EnsureAvailable();
    }

    [Test]
    public void SignedDisplacement_MapResolvesNull_PassesSourceThroughUnchanged()
    {
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap plain = RenderChain(makeEffect: null);
            using Bitmap present = RenderChain(MakeSignedTranslateEffect);
            using Bitmap absent = RenderWithForcedNullMap(MakeSignedTranslateEffect);

            double presentWarp = MeanChannelDiff(plain, present);
            double absentDiff = MeanChannelDiff(plain, absent);
            TestContext.WriteLine($"present warp = {presentWarp:F3}; absent-vs-identity diff = {absentDiff:F3}");

            Assert.That(presentWarp, Is.GreaterThan(10.0),
                "sanity: a present signed map must genuinely displace the source (else the identity check is vacuous)");
            Assert.That(absentDiff, Is.LessThanOrEqualTo(2.0),
                "a signed displacement whose map resolves null at render time must pass the source through unchanged, "
                + "not apply a full negative shift");
        });
    }

    [Test]
    public void MapPresenceBindingThrows_DisposesPendingShaderBeforeChildTake()
    {
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var injected = new InvalidOperationException("simulated map-presence uniform bind failure");
            DisplacementMapTransform.ForceMapPresenceBindFailureForTests(injected);
            try
            {
                InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
                    () => RenderChain(MakeSignedTranslateEffect));
                SKShader? pending = DisplacementMapTransform.MapShaderBuiltForTests;
                Assert.Multiple(() =>
                {
                    Assert.That(actual, Is.SameAs(injected));
                    Assert.That(pending, Is.Not.Null, "the test seam must fail after the map shader is built");
                    Assert.That(pending!.Handle, Is.EqualTo(IntPtr.Zero),
                        "a shader not yet transferred through ChildBinding.Take must be disposed on bind failure");
                });
            }
            finally
            {
                DisplacementMapTransform.ResetMapPresenceBindFailureForTests();
            }
        });
    }

    private static DisplacementMapEffect MakeSignedTranslateEffect()
    {
        var effect = new DisplacementMapEffect
        {
            DisplacementMap = { CurrentValue = HorizontalRamp() },
            Signed = { CurrentValue = true },
            Channel = { CurrentValue = DisplacementMapChannel.Red },
        };
        effect.Transform.CurrentValue = new DisplacementMapTranslateTransform
        {
            X = { CurrentValue = 40 },
            Y = { CurrentValue = 30 },
        };
        return effect;
    }

    private static LinearGradientBrush HorizontalRamp()
    {
        var map = new LinearGradientBrush();
        map.StartPoint.CurrentValue = new RelativePoint(0, 0, RelativeUnit.Relative);
        map.EndPoint.CurrentValue = new RelativePoint(1, 0, RelativeUnit.Relative);
        map.GradientStops.Add(new GradientStop(Colors.Black, 0));
        map.GradientStops.Add(new GradientStop(Colors.White, 1));
        return map;
    }

    private static RenderNodeOperation ShapeInput()
    {
        return RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas =>
            {
                canvas.DrawRectangle(s_bounds, Brushes.Resource.White, null);
                canvas.DrawRectangle(new Rect(30, 24, 50, 40), Brushes.Resource.Red, null);
                canvas.DrawRectangle(new Rect(90, 60, 40, 44), Brushes.Resource.Blue, null);
            },
            hitTest: s_bounds.Contains);
    }

    private static Bitmap RenderWithForcedNullMap(Func<DisplacementMapEffect> makeEffect)
    {
        DisplacementMapTransform.ForceMapShaderNullForTests();
        try
        {
            return RenderChain(makeEffect);
        }
        finally
        {
            DisplacementMapTransform.ResetMapShaderForTests();
        }
    }

    private static Bitmap RenderChain(Func<DisplacementMapEffect>? makeEffect)
    {
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        if (makeEffect != null)
        {
            FilterEffect effect = makeEffect();
            effect.Describe(builder, (FilterEffect.Resource)(object)effect.ToResource(CompositionContext.Default));
        }

        using EffectGraph graph = builder.Build();
        using var pool = new RenderTargetPool();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);
        RenderNodeOperation[] ops = PlanExecutor.Execute(
            plan, frame, [ShapeInput()], outputScale: 1f, workingScale: 1f,
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
        return target.Snapshot();
    }

    private static double MeanChannelDiff(Bitmap a, Bitmap b)
    {
        long sum = 0;
        long count = 0;
        for (int y = WinY0; y < WinY1; y++)
        {
            for (int x = WinX0; x < WinX1; x++)
            {
                SkiaSharp.SKColor ca = a.SKBitmap.GetPixel(x, y);
                SkiaSharp.SKColor cb = b.SKBitmap.GetPixel(x, y);
                sum += Math.Abs(ca.Red - cb.Red);
                sum += Math.Abs(ca.Green - cb.Green);
                sum += Math.Abs(ca.Blue - cb.Blue);
                count += 3;
            }
        }

        return count == 0 ? 0 : (double)sum / count;
    }
}
