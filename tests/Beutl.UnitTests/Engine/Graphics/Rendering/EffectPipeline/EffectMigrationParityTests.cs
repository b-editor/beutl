using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// The premultiplied-alpha migration parity gate (feature 004, T031, contracts/observability.md O4). Each case
/// renders a semitransparent alpha-gradient input through the migrated effect's new fused/mixed executor path and
/// through the retained legacy <see cref="FilterEffectActivator"/> (its <c>ApplyTo</c> recording), then asserts
/// the two match within the golden thresholds. This exercises the unpremultiply/re-premultiply handling of fused
/// shader/color-filter interleavings that the opaque frozen-reference gate (EffectReferenceFreezeTests) cannot.
/// </summary>
[NonParallelizable]
[TestFixture]
public class EffectMigrationParityTests
{
    private static readonly Rect s_bounds = new(0, 0, 192, 108);

    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
    }

    public static IEnumerable<TestCaseData> MigratedEffects()
    {
        yield return Case("Gamma", () => { var e = new Gamma(); e.Amount.CurrentValue = 1.5f; return e; });
        yield return Case("Invert", () => { var e = new Invert(); e.Amount.CurrentValue = 1f; return e; });
        yield return Case("Threshold", () => { var e = new Threshold(); e.Value.CurrentValue = 0.5f; e.Smoothness.CurrentValue = 20f; return e; });
        yield return Case("Negaposi", () => { var e = new Negaposi(); e.Red.CurrentValue = 255; e.Strength.CurrentValue = 1f; return e; });
        yield return Case("ChromaKey", () => { var e = new ChromaKey(); e.Color.CurrentValue = Colors.Lime; e.HueRange.CurrentValue = 30f; return e; });
        yield return Case("ColorKey", () => { var e = new ColorKey(); e.Color.CurrentValue = Colors.Lime; e.Range.CurrentValue = 20f; return e; });
        yield return Case("ColorGrading", () =>
        {
            var e = new ColorGrading();
            e.Contrast.CurrentValue = 1.2f;
            e.Saturation.CurrentValue = 1.3f;
            e.Exposure.CurrentValue = 0.2f;
            return e;
        });
        yield return Case("Curves", () =>
        {
            var e = new Curves();
            e.MasterCurve.CurrentValue = new CurveMap(
                [new CurveControlPoint(0, 0), new CurveControlPoint(0.5f, 0.72f), new CurveControlPoint(1, 1)]);
            return e;
        });
        yield return Case("LutEffect", () => { var e = new LutEffect(); e.Source.CurrentValue = SceneFixtures.CreateInvertLutSource(); return e; });
        yield return Case("Saturate", () => { var e = new Saturate(); e.Amount.CurrentValue = 1.5f; return e; });
        yield return Case("HueRotate", () => { var e = new HueRotate(); e.Angle.CurrentValue = 90f; return e; });
        yield return Case("Brightness", () => { var e = new Brightness(); e.Amount.CurrentValue = 1.2f; return e; });
        yield return Case("HighContrast", () => { var e = new HighContrast(); e.Contrast.CurrentValue = 0.5f; return e; });
        yield return Case("Lighting", () =>
        {
            var e = new Lighting();
            e.Multiply.CurrentValue = Colors.LightGray;
            e.Add.CurrentValue = Color.FromArgb(255, 20, 20, 20);
            return e;
        });
        yield return Case("LumaColor", () => new LumaColor());
    }

    [TestCaseSource(nameof(MigratedEffects))]
    public void Effect_FusedMatchesLegacyOnSemitransparentInput(string name, Func<FilterEffect> makeEffect)
    {
        AssertChainParity([makeEffect()]);
    }

    // Five invariant color effects fuse into one pass; parity against the legacy activator confirms the composed
    // premultiplied shader tree equals the sequenced legacy result on alpha-gradient content.
    [Test]
    public void ColorChain_FusedMatchesLegacyOnSemitransparentInput()
    {
        AssertChainParity(
        [
            new Gamma { Amount = { CurrentValue = 1.5f } },
            new HueRotate { Angle = { CurrentValue = 90f } },
            new Saturate { Amount = { CurrentValue = 1.4f } },
            new Brightness { Amount = { CurrentValue = 1.2f } },
            new Invert { Amount = { CurrentValue = 1f } },
        ]);
    }

    // Blur and DropShadow are still unmigrated, so this drives the mixed-plan executor (opaque segments around
    // fused runs) on semitransparent content.
    [Test]
    public void MixedChain_FusedMatchesLegacyOnSemitransparentInput()
    {
        AssertChainParity(
        [
            new Blur { Sigma = { CurrentValue = new Size(6, 6) } },
            new Gamma { Amount = { CurrentValue = 1.4f } },
            new Invert { Amount = { CurrentValue = 1f } },
            new DropShadow { Position = { CurrentValue = new Point(8, 8) }, Sigma = { CurrentValue = new Size(6, 6) }, Color = { CurrentValue = Colors.Black } },
            new LutEffect { Source = { CurrentValue = SceneFixtures.CreateInvertLutSource() } },
        ]);
    }

    private static void AssertChainParity(IReadOnlyList<FilterEffect> effects)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap fused = RenderChain(effects, fused: true);
            using Bitmap legacy = RenderChain(effects, fused: false);

            double ssim = ImageMetrics.Ssim(legacy, fused);
            double mae = ImageMetrics.MeanAbsoluteError(legacy, fused);
            TestContext.WriteLine($"fused-vs-legacy SSIM={ssim:F4} MAE={mae:F4}");
            Assert.Multiple(() =>
            {
                Assert.That(ssim, Is.GreaterThanOrEqualTo(GoldenThresholds.ExactSsimMin), $"SSIM {ssim}");
                Assert.That(mae, Is.LessThanOrEqualTo(GoldenThresholds.ExactMaeMax), $"MAE {mae}");
            });
        });
    }

    private static Bitmap RenderChain(IReadOnlyList<FilterEffect> effects, bool fused)
    {
        RenderNodeOperation[] inputs = [MakeSemitransparentInput(s_bounds)];
        RenderNodeOperation[] outputs;
        if (fused)
        {
            var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);
            foreach (FilterEffect effect in effects)
            {
                FilterEffect.Resource resource = Capture(effect);
                effect.Describe(builder, resource);
            }

            using EffectGraph graph = builder.Build();
            CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
            FrameResources frame = EffectGraphCompiler.ResolveResources(plan, s_bounds, workingScale: 1f);
            outputs = PlanExecutor.Execute(
                plan, frame, inputs, s_bounds, outputScale: 1f, workingScale: 1f,
                maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null);
        }
        else
        {
            using var context = new FilterEffectContext(s_bounds, outputScale: 1f, workingScale: 1f);
            foreach (FilterEffect effect in effects)
            {
                effect.ApplyTo(context, Capture(effect));
            }

            outputs = LegacyBridgeExecutor.Execute(
                context, inputs, s_bounds, outputScale: 1f, workingScale: 1f,
                maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null);
        }

        return Rasterize(outputs, s_bounds);
    }

    private static FilterEffect.Resource Capture(FilterEffect effect)
        => (FilterEffect.Resource)(object)effect.ToResource(CompositionContext.Default);

    // Concentric alpha-gradient regions over a transparent border: the α ∈ {0, 0.25, 0.5, 1} bands exercise both
    // the α ≤ ε early-out and mid-alpha unpremultiply of every migrated snippet.
    private static RenderNodeOperation MakeSemitransparentInput(Rect bounds)
    {
        return RenderNodeOperation.CreateLambda(
            bounds,
            canvas =>
            {
                canvas.DrawRectangle(bounds.Deflate(10), Fill(128, 0, 255, 0), null);
                canvas.DrawRectangle(bounds.Deflate(35), Fill(64, 255, 0, 0), null);
                canvas.DrawRectangle(bounds.Deflate(60), Fill(255, 0, 0, 255), null);
            },
            hitTest: bounds.Contains);
    }

    private static Brush.Resource Fill(byte a, byte r, byte g, byte b)
        => (Brush.Resource)new SolidColorBrush(Color.FromArgb(a, r, g, b)).ToResource(CompositionContext.Default);

    private static Bitmap Rasterize(RenderNodeOperation[] ops, Rect bounds)
    {
        var size = PixelRect.FromRect(bounds);
        using RenderTarget target = RenderTarget.Create(size.Width, size.Height)
            ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
        using (var canvas = new ImmediateCanvas(target, 1f, logicalSize: bounds.Size))
        {
            canvas.Clear();
            using (canvas.PushTransform(Matrix.CreateTranslation(-bounds.X, -bounds.Y)))
            {
                foreach (RenderNodeOperation op in ops)
                {
                    op.Render(canvas);
                }
            }
        }

        RenderNodeOperation.DisposeAll(ops);
        return target.Snapshot();
    }

    private static TestCaseData Case(string name, Func<FilterEffect> make)
        => new TestCaseData(name, make).SetName($"Effect_{name}");
}
