using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

/// <summary>
/// Pins that a symbolic <see cref="FilterEffectContext.Transform{T}(T, Func{T, Rect, Matrix}, BitmapInterpolationMode)"/>
/// reports the bounds it renders on every activation, not only the first one.
/// </summary>
/// <remarks>
/// The recorded item is shared by every activation of the context and of its <see cref="FilterEffectContext.Clone"/>,
/// while the Skia factory resolves its matrix from the execution-time targets each time. Any resolution state the
/// item keeps between activations therefore desynchronizes the reported bounds from the rendered pixels.
/// </remarks>
[TestFixture]
public sealed class SymbolicTransformActivationTests
{
    private static readonly Rect s_firstBounds = new(0, 0, 8, 6);
    private static readonly Rect s_secondBounds = new(40, 0, 8, 6);

    [Test]
    public void SymbolicTransform_AppliedTwice_ReportsTheBoundsEachActivationRenders()
    {
        using var context = new FilterEffectContext(Rect.Invalid);
        AppendBoundsDependentTranslation(context);

        Activation first = Activate(context, s_firstBounds);
        Activation second = Activate(context, s_secondBounds);

        AssertConsistent(first, second);
    }

    [Test]
    public void SymbolicTransform_ClonedAfterFirstActivation_ReportsTheBoundsItRenders()
    {
        using var context = new FilterEffectContext(Rect.Invalid);
        AppendBoundsDependentTranslation(context);

        Activation first = Activate(context, s_firstBounds);
        using FilterEffectContext clone = context.Clone();
        Activation second = Activate(clone, s_secondBounds);

        AssertConsistent(first, second);
    }

    private static void AssertConsistent(Activation first, Activation second)
    {
        TestContext.WriteLine($"first: reported={first.Reported} rendered={first.Rendered}");
        TestContext.WriteLine($"second: reported={second.Reported} rendered={second.Rendered}");
        Assert.Multiple(() =>
        {
            Assert.That(first.Rendered, Is.EqualTo(first.Reported),
                "the first activation must render into the bounds it reports.");
            Assert.That(second.Rendered, Is.EqualTo(second.Reported),
                "the second activation must render into the bounds it reports.");
            Assert.That(second.Reported, Is.EqualTo(s_secondBounds.Translate(new Vector(s_secondBounds.X, 0))),
                "the second activation must report the matrix resolved from its own target bounds.");
        });
    }

    // A translation whose offset is read from the activation's own target bounds: the first and the second
    // activation resolve different matrices, and a rigid translation keeps the rendered footprint exactly
    // equal to the reported one whenever both agree on the matrix.
    private static void AppendBoundsDependentTranslation(FilterEffectContext context)
        => context.Transform(
            0,
            static (_, bounds) => Matrix.CreateTranslation(bounds.X, 0),
            BitmapInterpolationMode.Default);

    private static Activation Activate(FilterEffectContext context, Rect bounds)
    {
        using EffectTargets targets = CreateSolidTargets(bounds);
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets,
            builder,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            drawableBrushMaterializer: null,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1);

        activator.Apply(context);
        activator.Flush();

        EffectTarget target = activator.CurrentTargets.Single();
        return new Activation(target.Bounds, MeasureCoveredBounds(target));
    }

    private static Rect MeasureCoveredBounds(EffectTarget target)
    {
        using Bitmap alpha = target.RenderTarget!.SnapshotAlpha();
        ReadOnlySpan<byte> pixels = alpha.GetPixelSpan<byte>();
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (int y = 0; y < alpha.Height; y++)
        {
            for (int x = 0; x < alpha.Width; x++)
            {
                if (pixels[(y * alpha.Width) + x] == 0)
                    continue;

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        if (minX > maxX)
            return default;

        float density = target.Scale.Value;
        Rect raster = target.RasterBounds;
        return new Rect(
            raster.X + (minX / density),
            raster.Y + (minY / density),
            (maxX - minX + 1) / density,
            (maxY - minY + 1) / density);
    }

    private static EffectTargets CreateSolidTargets(Rect bounds)
    {
        using var backing = new CpuRenderTarget((int)bounds.Width, (int)bounds.Height);
        backing.Value.Canvas.Clear(new SKColor(255, 255, 255, 255));
        backing.Value.Canvas.Flush();

        return new EffectTargets
        {
            new EffectTarget(backing, bounds, EffectiveScale.At(1)),
        };
    }

    private readonly record struct Activation(Rect Reported, Rect Rendered);

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(CreateSurface(width, height), width, height)
    {
        private static SKSurface CreateSurface(int width, int height)
            => SKSurface.Create(new SKImageInfo(
                   width,
                   height,
                   SKColorType.RgbaF16,
                   SKAlphaType.Premul,
                   SKColorSpace.CreateSrgbLinear()))
               ?? throw new InvalidOperationException("A CPU effect-test surface could not be created.");
    }
}
